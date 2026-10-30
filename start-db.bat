@echo off
echo Starting PostgreSQL via Docker Compose...
cd docker
docker compose up -d
echo Database should be available on localhost:55432 (user: enova / password: 1234)
pause
