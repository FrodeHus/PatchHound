ui = true

api_addr = "http://openbao:8200"
cluster_addr = "http://openbao:8201"
disable_mlock = true

storage "file" {
  path = "/openbao/file"
}

listener "tcp" {
  address = "0.0.0.0:8200"
  tls_disable = 1
}
