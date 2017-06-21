# cake

Write a _Cakefile_:

```
$ cat Cakefile
size 1024

ca Cake Corporation Internal CA
  email root@cake.com

domain www.cake.com
  email webmaster@cake.com
  size 4096

domain mail.example.com
  email postmaster@cake.com
  alt imap.cake.com
  alt smtp.cake.com
```


Run _cake_:

```
$ cake
🍰  new CA key
Generating RSA private key, 1024 bit long modulus
......++++++
[...]
🍰  summary:
🍰  new CA key
🍰  new CA cert
🍰  new key for www.cake.com
🍰  new cert for www.cake.com
🍰  new key for mail.cake.com
🍰  new cert for mail.cake.com
🍰  all up to date!
```


Behold _files_:

```
$ ls
Cakefile
ca.cert.pem
ca.key.pem
mail.cake.com.cert.pem
mail.cake.com.key.pem
www.cake.com.cert.pem
www.cake.com.key.pem
```
