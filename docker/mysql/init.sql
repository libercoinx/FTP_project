CREATE DATABASE IF NOT EXISTS ftp_client
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_0900_ai_ci;

GRANT ALL PRIVILEGES ON ftp_client.* TO 'ftp_app'@'%';
FLUSH PRIVILEGES;

USE ftp_client;

CREATE TABLE IF NOT EXISTS sites (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    host VARCHAR(255) NOT NULL,
    port INT NOT NULL,
    username VARCHAR(255) NOT NULL,
    protected_password TEXT NULL,
    remember_password BOOLEAN NOT NULL DEFAULT FALSE,
    updated_at DATETIME(6) NOT NULL,
    UNIQUE KEY uq_site_endpoint (host, port, username)
);

CREATE TABLE IF NOT EXISTS transfer_tasks (
    id CHAR(36) NOT NULL PRIMARY KEY,
    site_id BIGINT NULL,
    direction VARCHAR(16) NOT NULL,
    local_path TEXT NOT NULL,
    remote_path TEXT NOT NULL,
    total_bytes BIGINT NOT NULL,
    transferred_bytes BIGINT NOT NULL,
    status VARCHAR(16) NOT NULL,
    error_message TEXT NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    INDEX ix_transfer_status (status)
);
