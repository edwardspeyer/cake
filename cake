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

  $ cat Cakefile
  ca Cake Example Certificate Authority
    email root@cake.com

  domain www.cake.com
    size 1024
    email webmaster@cake.com

  domain mail.cake.com
    email postmaster@cake.com
    alt imap.cake.com
    alt smtp.cake.com


Step 2: run cake, get lots of output:

  $ cake
  [MUCH OUTPUT]
  cake: summary:
  cake: new CA key
  cake: new CA cert
  cake: new key for www.example.com
  cake: new cert for www.example.com
  cake: new key for mail.example.com
  cake: new cert for mail.example.com
  cake: all up to date!


Warnings:
  - Generated keys don't bother with passphrases!
  - Only use cake on hardware you trust!
  - Lots of output!
  - No support for anything other than CN=<fqdn> with DNS: subjectAltNames!
  - Who uses anything else?!
  - LOTS OF OUTPUT!
  - Not hard to use, it's piece of cake!

"

#
# OpenSSL config file, where most options live.
#
OPENSSL_CNF_MAIN="
[ ca ]
default_ca = ca_CAKE

[ ca_CAKE ]
dir           = .
new_certs_dir = .
database      = index.txt
serial        = serial
private_key   = ca.key.pem
certificate   = ca.cert.pem
default_md    = sha256
name_opt      = ca_default
cert_opt      = ca_default
default_days  = 3650
preserve      = no
policy        = policy_loose

[ policy_loose ]
commonName    = supplied
# TODO make this supplied
emailAddress  = optional

[ req ]
distinguished_name  = blank_distinguished_name
string_mask         = utf8only
default_md          = sha256
x509_extensions     = v3_ca

[ blank_distinguished_name ]
# With the use of -subj on the command line, I don't believe this is ever
# consulted, so we leave it blank.

[ v3_ca ]
subjectKeyIdentifier    = hash
authorityKeyIdentifier  = keyid:always,issuer
basicConstraints        = critical, CA:true
keyUsage                = critical, digitalSignature, cRLSign, keyCertSign
"

#
# Additional config, required for making server certificates.
#
OPENSSL_CNF_DOMAIN="
$OPENSSL_CNF_MAIN

[ domain_cert ]
basicConstraints        = CA:FALSE
nsCertType              = server
nsComment               = 'OpenSSL Generated Server Certificate'
subjectKeyIdentifier    = hash
authorityKeyIdentifier  = keyid,issuer:always
keyUsage                = critical, digitalSignature, keyEncipherment
extendedKeyUsage        = serverAuth
# See [SANENV] below...
subjectAltName          = \${ENV::subject_alt_name}
"

#
# Main entry point: parse options into globals, set up a TMP that looks a bit
# like a standard OpenSSL certificate authority directory, then parse a
# cakefile.
#
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
      summary
      say "all up to date!"
      exit
    fi
  done

  # Only called if no candidate cakefile was found.
  usage
}

usage() {
  echo "$HELP"
  exit 1
}


#
# Logging functions
#

# Defined as a global rather than in say() because we pipe say() into other
# things, internally.
IS_TTY=''
if [ -t 1 ]
then
  IS_TTY=1
fi

#
# Print a message to stdout that stands out from other output.  OpenSSL is
# verbose and it is hard to see what is going on.
#
say() {
  if [ "$IS_TTY" ]
  then
    echo "🍰  $*"
  else
    echo "cake: $*"
  fi
}

#
# Log message to stdout and a log file.
#
log() {
  say "$*" | tee -a $TMP/log
}

#
# Replay everything that was logged
#
summary() {
  if [ -f $TMP/log ]
  then
    say "summary:"
    cat $TMP/log
  fi
}

#
# Log to stderr.
#
warn() {
  say "$*" >&2
}

#
# Warn then die.
#
fatal() {
  warn "$*"
  exit 1
}

#
# For a given name, delete name.cert.pem if its corresponding name.key.pem is
# either missing or newer (on the assumption that the key has been regenerated,
# so the cert is now out of date.)
#
prune_cert() {
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

#
# For a given name, delete name.key.pem and name.cert.pem if they are older
# than the CA cert or CA key (on the assumption that the CA is different to the
# one which created this key/cert pair.)
#
prune_name() {
  local name=$1
  for file in $name.key.pem $name.cert.pem
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

#
# Execute a cakefile line by line.
#
parse() {
  local cakefile="$1"

  local global_key_size=4096
  local global_email=''

  # Set the current state, and reset all other cursors to their global values.
  state() {
    local state="$1"
    cur_alternates=''
    cur_cn=''
    cur_email=''
    cur_key_size=$global_key_size
    cur_state="$state"
  }

  # Use the cursors based on whatever the current state is.
  commit() {
    case $cur_state in
      globals)
        if [ "$cur_email" ]
        then
          global_email="$cur_email"
        fi

        if [ "$cur_key_size" ]
        then
          global_key_size=$cur_key_size
        fi
        ;;

      ca_state)
        build_ca "$cur_cn" "$cur_email" "$cur_key_size"
        ;;

      domain_state)
        build_domain "$cur_cn" "$cur_email" "$cur_key_size" "$cur_alternates"
        ;;
    esac
  }

  # Begin in the "globals" state.
  #
  # Each "ca", "domain" etc. (or EOF) causes the previous state to be committed
  # and the state to be reset.
  state 'globals'

  strip "$cakefile" | {
    while read command args
    do
      case "$command" in
        ca)
          commit
          state 'ca_state'
          cur_cn="$args"
          ;;

        size)
          cur_key_size="$args"
          if ! echo $cur_key_size | egrep -q '(1024|2048|4096)'
          then
            fatal "weird key size: $cur_key_size"
          fi
          ;;

        email)
          cur_email="$args"
          ;;

        domain)
          commit
          state 'domain_state'
          cur_cn="$args"
          ;;

        alt)
          cur_alternates="$cur_alternates $args"
          ;;

        *)
          warn "unknown command $command"
          ;;
      esac
    done

    commit
  }
}

#
# Remove comments and empty lines.
#
strip() {
  sed -e 's/ *#.*$//' $1 | grep -v '^$'
}

#
# Create an X509(?) subject from a common-name and email-address.
#
subject() {
  local cn="$1"
  local email="$2"

  if [ -z "$cn" ]
  then
    fatal "subject CN can't be empty"
  fi

  if [ "$email" ]
  then
    echo "/emailAddress=$email/CN=$cn"
  else
    echo "/CN=$cn"
  fi
}

#
# Build the CA cert, which is just a sort of certificate signing request which
# isn't signed, I think?
#
build_ca() {
  local cn="$1"
  local email="$2"
  local key_size="$3"

  prune_cert ca

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
      subject=$(subject "$cn" "$email")
      openssl req               \
        -config main.config     \
        -key ca.key.pem         \
        -new                    \
        -x509                   \
        -days 7300              \
        -sha256                 \
        -extensions v3_ca       \
        -subj "$subject"        \
        -out ca.cert.pem
    )
    cp $TMP/ca.cert.pem .
  fi
}

#
# Build a key and cert for an FQDN with alternate names.
#
build_domain() {
  local domain="$1"
  local email="$2"
  local key_size="$3"
  local alternates="$4"

  prune_cert $domain
  prune_name $domain

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

      # OpenSSL can't be passed a subject-alt-name on the command line, and we
      # can't hard code it into the config file, so we pass it in using the
      # environment instead. [SANENV]
      export subject_alt_name

      subject=$(subject $domain "$email")
      openssl req               \
        -config domain.config   \
        -key $domain.key.pem    \
        -extensions domain_cert \
        -subj "$subject"        \
        -new                    \
        -sha256                 \
        -out $domain.csr.pem

      yes | openssl ca          \
        -config domain.config   \
        -extensions domain_cert \
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
