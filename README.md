# cake 🍰 certificate authority

Write a _Cakefile_ describing your CA and the certificates you need, then build everything with _cake_:

```
£ ls
Cakefile


£ cat Cakefile
ca Cake Corporation Internal CA
  email root@cake.com

domain www.cake.com
  email webmaster@cake.com
  size 1024

domain mail.example.com
  email postmaster@cake.com
  alt imap.cake.com
  alt smtp.cake.com


£ cake
🍰  new CA key
Generating RSA private key, 4096 bit long modulus
[...]
🍰  summary:
🍰  new CA key
🍰  new CA cert
🍰  new key for www.cake.com
🍰  new cert for www.cake.com
🍰  new key for mail.cake.com
🍰  new cert for mail.cake.com
🍰  all up to date!


£ ls
Cakefile
ca.cert.pem
ca.key.pem
mail.cake.com.cert.pem
mail.cake.com.key.pem
www.cake.com.cert.pem
www.cake.com.key.pem
```

# Warnings / Boasts

* Generated keys don't bother with passphrases
* Only use cake on hardware you trust, I guess?
* Lots of output from _openssl_ passed directly to you, unedited
* No support for anything other than _CN=[fqdn]_ with _DNS:..._ subjectAltNames!
* But who uses anything else?!
* Maybe it should support user certificates?
* Full support for using weak 1024 bit keys though
* Not hard to use: it's piece of cake!


# Acknowledgments

* Jamie Nguyen's [excellent documentation][JL] on how to do this properly, by hand.
* Debian Administration's [guide][DA] to using Eric Young and Tim Hudson's
  original scripts (1996!) that ship with OpenSSL.  If it ain't broke, rewrite
  it.
* Stack Excchange [answer][SE] on passing subjectAltName as an environment
  variable, instead of hard-coding in the config file.

[JL]: https://jamielinux.com/docs/openssl-certificate-authority/
[SE]: https://security.stackexchange.com/questions/74345/
[DA]: https://debian-administration.org/article/618/Certificate_Authority_CA_with_OpenSSL
