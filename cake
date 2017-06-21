#!/bin/bash
#
# cake: creating a certificate authority is a piece of cake!
#
# https://jamielinux.com/docs/openssl-certificate-authority/
# https://security.stackexchange.com/questions/74345/
#

HELP="\
usage: cake [options]

Options:
  Just kidding!  No options!

Step 1: make a directory with a Cakefile:

  # cat Cakefile
  size 1024
  subject /CN=Cake Example Certificate Authority

  domain www.example.com
    size 2048

  domain mail.example.com
    size 1024
    alt imap.example.com
    alt smtp.example.com


Step 2: run cake, get lots of output:

  $ cake
  🍰  new CA key
  Generating RSA private key, 1024 bit long modulus
  .++++++
  ........................++++++
  [MUCH OUTPUT]
  🍰  recap:
  🍰  new CA key
  🍰  new CA cert
  🍰  new key for www.example.com
  🍰  new cert for www.example.com
  🍰  new key for mail.example.com
  🍰  new cert for mail.example.com
  🍰  all up to date!


Warnings:
  - Generated keys don't bother with passphrases; use cake on hardware you
    completely trust!
  - Lots of output!
  - No support for anything other than CN=<fqdn> with DNS: subjectAltNames!
  - Who uses anything else?!
  - LOTS OF OUTPUT!
  - Not hard to use, it's piece of cake!

"

OPENSSL_CNF_MAIN="
[ ca ]
default_ca = ca_CAKE

[ ca_CAKE ]
dir               = .
new_certs_dir     = .
database          = index.txt
serial            = serial
private_key       = ca.key.pem
certificate       = ca.cert.pem
default_md        = sha256
name_opt          = ca_default
cert_opt          = ca_default
default_days      = 3650
preserve          = no
policy            = policy_loose

[ policy_loose ]
countryName             = optional
stateOrProvinceName     = optional
localityName            = optional
organizationName        = optional
organizationalUnitName  = optional
commonName              = supplied
emailAddress            = optional

[ req ]
distinguished_name  = req_distinguished_name
string_mask         = utf8only
default_md          = sha256
x509_extensions     = v3_ca

[ req_distinguished_name ]
countryName                     = UK
stateOrProvinceName             = State or Province Name
localityName                    = Locality Name
0.organizationName              = Organization Name
organizationalUnitName          = Organizational Unit Name
commonName                      = Common Name
emailAddress                    = Email Address

[ v3_ca ]
subjectKeyIdentifier = hash
authorityKeyIdentifier = keyid:always,issuer
basicConstraints = critical, CA:true
keyUsage = critical, digitalSignature, cRLSign, keyCertSign
"

OPENSSL_CNF_DOMAIN="
$OPENSSL_CNF_MAIN

[ server_cert ]
basicConstraints = CA:FALSE
nsCertType = server
nsComment = 'OpenSSL Generated Server Certificate'
subjectKeyIdentifier = hash
authorityKeyIdentifier = keyid,issuer:always
keyUsage = critical, digitalSignature, keyEncipherment
extendedKeyUsage = serverAuth
subjectAltName = \${ENV::subject_alt_name}
"


if [ -t 1 ]
then
  IS_TTY=1
fi

main() {
  case "$*" in
    *-h*)
      usage
  esac

  # Prepare the TMP area to look a bit like a working OpenSSL CA directory.
  TMP="$(mktemp -d -t cake-certificate-authority)"
  trap "rm -rf $TMP" EXIT
  (
    cd $TMP
    touch index.txt
    echo 1001 > serial
    echo "$OPENSSL_CNF_MAIN"    > main.config
    echo "$OPENSSL_CNF_DOMAIN"  > domain.config
  )
  
  for candidate in "$1" cakefile Cakefile
  do
    if [ -f "$candidate" ]
    then
      parse "$candidate"
      recap
      say "all up to date!"
      exit
    fi
  done

  usage
}

usage() {
  echo "$HELP"
  exit 1
}

say() {
  if [ "$IS_TTY" ]
  then
    echo "🍰  $*"
  else
    echo "cake: $*"
  fi
}

log() {
  say "$*" | tee >&2 -a $TMP/log
}

warn() {
  say "$*" >&2
}

fatal() {
  warn "$*"
  exit 1
}

recap() {
  if [ -f $TMP/log ]
  then
    say "recap:"
    cat $TMP/log
  fi
}

prune_pair() {
  local key=$1.key.pem
  local cert=$1.cert.pem

  if [ $key -nt $cert ]
  then
    log "out-of-date cert '$cert' older than key '$key'"
    rm -f $cert
  fi

  if [ -f $cert ] && [ ! -f $key ]
  then
    log "abandoned cert '$cert'; key '$key' is missing"
    rm -f $cert
  fi
}

prune_domain() {
  local domain=$1
  for file in $domain.key.pem $domain.cert.pem
  do
    if [ ! -f $file ]
    then
      continue
    fi

    if [ $file -ot ca.cert.pem ] || [ $file -ot ca.key.pem ]
    then
      log "pruning out of date file $file"
      rm $file
    fi
  done
}

parse() {
  local cakefile="$1"

  ca_subject=''
  key_size=4096
  domain=''
  alternates=''

  strip "$cakefile" | {
    while read command args
    do
      case "$command" in
        size)
          key_size="$args"
          if ! echo $key_size | egrep -q '(1024|2048|4096)'
          then
            fatal "weird key size: $key_size"
          fi
          ;;

        subject)
          build_ca "$args" "$key_size"
          ;;

        domain)
          if [ "$domain" ]
          then
            build_domain $domain $key_size "$alternates"
          fi
          domain="$args"
          ;;

        alt)
          alternates="$alternates $args"
          ;;

        *)
          warn "unknown command $command"
          ;;
      esac
    done

    if [ "$domain" ]
    then
      build_domain $domain $key_size "$alternates"
    fi
  }
}

strip() {
  sed -e 's/ *#.*$//' $1 | grep -v '^$'
}

ca_subject() {
  stripped_config | head -1
}

build_ca() {
  local ca_subject="$1"
  local key_size="$2"
  
  prune_pair ca

  if [ -f ca.key.pem ]
  then
    cp ca.key.pem $TMP
  else
    log "new CA key"
    (
      cd $TMP
      openssl genrsa -out ca.key.pem $key_size
      chmod 0600 ca.key.pem
    )
    cp $TMP/ca.key.pem .
  fi

  if [ -f ca.cert.pem ]
  then
    cp ca.cert.pem $TMP
  else
    log "new CA cert"
    (
      cd $TMP
      openssl req               \
        -config main.config     \
        -key ca.key.pem         \
        -new                    \
        -x509                   \
        -days 7300              \
        -sha256                 \
        -extensions v3_ca       \
        -subj "$ca_subject"     \
        -out ca.cert.pem
    )
    cp $TMP/ca.cert.pem .
  fi
}

build_domain() {
  local domain=$1
  local key_size=$2
  local alternates="$3"

  prune_pair $domain
  prune_domain $domain

  subject_alt_name="DNS:$domain"
  for alternate in $alternates
  do
    subject_alt_name="$subject_alt_name,DNS:$alternate"
  done

  if [ -f $domain.key.pem ]
  then
    cp $domain.key.pem $TMP/
  else
    log "new key for $domain"
    (
      cd $TMP
      openssl genrsa -out $domain.key.pem $key_size
      chmod 0600 $domain.key.pem
    )
    cp $TMP/$domain.key.pem .
  fi

  if [ -f $domain.cert.pem ]
  then
    cp $domain.cert.pem $TMP/
  else
    log "new cert for $domain"
    (
      cd $TMP
      export subject_alt_name

      openssl req               \
        -config domain.config   \
        -key $domain.key.pem    \
        -extensions server_cert \
        -subj "/CN=$domain"     \
        -new                    \
        -sha256                 \
        -out $domain.csr.pem

      yes | openssl ca          \
        -config domain.config   \
        -extensions server_cert \
        -notext                 \
        -md sha256              \
        -in $domain.csr.pem     \
        -out $domain.cert.pem

    )
    cp $TMP/$domain.cert.pem .
  fi
}

set -e

main "$@"
