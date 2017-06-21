#!/bin/bash
#
# cake: creating a certificate authority is a piece of cake!
#
# https://jamielinux.com/docs/openssl-certificate-authority/
# https://security.stackexchange.com/questions/74345/
#

HELP="\
usage: cake <cakefile-path>

Or run cake in a directory containing a Cakefile, where the Cakefile format is
as follows:

  /CN=Your Certificate Authority's Subject Name on the first line
  # Followed by comments and groups of domains
  a.example.com
  # The first domain is the CN, all others are subjectAltNames
  b.example.com alias.example.com

The current working directory will be popualated with (password-free!) keys and
certificates.

Piece of cake!
"

#KEY_SIZE=4096
KEY_SIZE=1024

if [ -t 1 ]
then
  IS_TTY=1
fi

main() {
  case "$*" in
    *-h*)
      usage
  esac

  TMP="$(mktemp -d -t cake-certificate-authority)"
  trap "rm -rf $TMP" EXIT

  BASE="$(pwd)"
  for candidate in "$1" cakefile Cakefile
  do
    if [ -f "$candidate" ]
    then
      CONFIG="$BASE/$candidate"
      prune
      build
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

recap() {
  if [ -f $TMP/log ]
  then
    say "recap:"
    cat $TMP/log
  fi
}

prune() {
  prune_pair 'ca'
  for_each_domain prune_pair
  for_each_domain prune_domain
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

build() {
  (
    cd $TMP
    touch index.txt
    echo 1001 > serial
    echo "$OPENSSL_CNF" > openssl.cnf
  )

  # TODO can I get away with not doing this?
  export subject_alt_name='n/a'

  build_ca
  for_each_domain build_domain
}

stripped_config() {
  sed -e 's/ *#.*$//' $CONFIG | grep -v '^$'
}

ca_subject() {
  stripped_config | head -1
}

for_each_domain() {
  stripped_config | tail -n +2 |
    while read domain alternates
    do
      subject_alt_name="DNS:$domain"
      for alternate in $alternates
      do
        subject_alt_name="${subject_alt_name},DNS:$alternate"
      done

      "$1" $domain $subject_alt_name
    done
}

build_ca() {
  if [ -f ca.key.pem ]
  then
    cp ca.key.pem $TMP
  else
    log "new CA key"
    (
      cd $TMP
      openssl genrsa -out ca.key.pem $KEY_SIZE
      chmod 0600 ca.key.pem
      cp ca.key.pem $BASE
    )
  fi

  if [ -f ca.cert.pem ]
  then
    cp ca.cert.pem $TMP
  else
    log "new CA cert"
    (
      cd $TMP
      openssl req               \
        -config openssl.cnf     \
        -key ca.key.pem         \
        -new                    \
        -x509                   \
        -days 7300              \
        -sha256                 \
        -extensions v3_ca       \
        -subj "$(ca_subject)"   \
        -out ca.cert.pem
      cp ca.cert.pem $BASE
    )
  fi
}

build_domain() {
  local domain=$1
  local subject_alt_name=$2

  if [ -f $domain.key.pem ]
  then
    cp $domain.key.pem $TMP/
  else
    log "new key for $domain"
    (
      cd $TMP
      openssl genrsa -out $domain.key.pem $KEY_SIZE
      chmod 0600 $domain.key.pem
      cp $domain.key.pem $BASE
    )
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
        -config openssl.cnf     \
        -key $domain.key.pem    \
        -extensions server_cert \
        -subj "/CN=$domain"     \
        -new                    \
        -sha256                 \
        -out $domain.csr.pem

      # TODO can I put -days in the config?
      # TODO do I really want -notext?
      yes | openssl ca          \
        -config openssl.cnf     \
        -extensions server_cert \
        -days 3750              \
        -notext                 \
        -md sha256              \
        -in $domain.csr.pem     \
        -out $domain.cert.pem

      cp $domain.cert.pem $BASE
    )
  fi
}

OPENSSL_CNF="
[ ca ]
default_ca = ca_HAZ

[ ca_HAZ ]
dir               = .
new_certs_dir     = .
database          = index.txt
serial            = serial
private_key       = ca.key.pem
certificate       = ca.cert.pem
default_md        = sha256
name_opt          = ca_default
cert_opt          = ca_default
default_days      = 3750
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
default_bits        = $KEY_SIZE
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

set -e

main "$@"
