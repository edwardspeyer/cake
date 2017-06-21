#!/bin/bash
#
# cake: creating a certificate authority is a piece of cake!
#
# https://jamielinux.com/docs/openssl-certificate-authority/
# https://security.stackexchange.com/questions/74345/
#

KEY_SIZE=4096

log() {
  echo -e >&2 "\033[4m$(basename "$0"): $*\033[0m"
}

set -e

if [ "$1" ]
then
  config="$1"
elif [ -f cakefile ]
then
  config=cakefile
elif [ -f Cakefile ]
then
  config=Cakefile
else
  cat >&2 <<...
usage: $(basename "$0") <cakefile-path>

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
...
  exit 2
fi

ca_subject="$(head -1 "$config")"

tmp=$(mktemp -d)

touch $tmp/index.txt
echo 1001 > $tmp/serial

export subject_alt_name='NONE'

cat > $tmp/openssl.cnf <<...
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
nsComment = "OpenSSL Generated Server Certificate"
subjectKeyIdentifier = hash
authorityKeyIdentifier = keyid,issuer:always
keyUsage = critical, digitalSignature, keyEncipherment
extendedKeyUsage = serverAuth
subjectAltName = \${ENV::subject_alt_name}
...

if [ -f ca.key.pem ]
then
  cp ca.key.pem $tmp/
else
  log "CA: new key"
  # Invalidate old CA cert
  rm -f ca.cert.pem
  (
    cd $tmp
    openssl genrsa \
      -out ca.key.pem $KEY_SIZE
    chmod 400 ca.key.pem
  )
  cp $tmp/ca.key.pem .
fi

if [ -f ca.cert.pem ]
then
  cp ca.cert.pem $tmp/ca.cert.pem
else
  log "CA: new certificate"
  (
    cd $tmp
    openssl req -config openssl.cnf \
      -key ca.key.pem \
      -new -x509 \
      -days 7300 \
      -sha256 \
      -extensions v3_ca \
      -subj "$ca_subject" \
      -out ca.cert.pem
  )
  cp $tmp/ca.cert.pem .
fi

tail -n +2 "$config" \
  | sed -e 's/ *#.*$//' \
  | grep -v '^$' \
  | while read domain alts
  do
    subject_alt_name="DNS:$domain"
    for alt in $alts
    do
      subject_alt_name="${subject_alt_name},DNS:$alt"
    done

    if [ -f $domain.key.pem ]
    then
      cp $domain.key.pem $tmp/
    else
      log "$domain: new key"
      # Invalidate old domain cert
      rm -f $domain.cert.pem
      (
        cd $tmp
        openssl genrsa \
          -out $domain.key.pem $KEY_SIZE
        chmod 0400 $domain.key.pem
      )
      cp $tmp/$domain.key.pem .
    fi

    if [ -f $domain.cert.pem ]
    then
      cp $domain.cert.pem $tmp/
    else
      log "$domain: new certificate"
      export subject_alt_name
      (
        cd $tmp
        openssl req -config openssl.cnf \
          -key $domain.key.pem \
          -extensions server_cert \
          -subj "/CN=$domain" \
          -new -sha256 -out $domain.csr.pem

        yes | openssl ca -config openssl.cnf \
          -extensions server_cert \
          -days 3750 \
          -notext \
          -md sha256 \
          -in $domain.csr.pem \
          -out $domain.cert.pem
      )
      cp $tmp/$domain.cert.pem .
    fi
  done
