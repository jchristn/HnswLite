@echo off
echo Running HNSW Index Server Docker container...

if not exist "data" mkdir data
if not exist "logs" mkdir logs

docker run -it --rm ^
  -p 8080:8080 ^
  -v "%cd%\hnswindex.json:/app/hnswindex.json" ^
  -v "%cd%\data:/app/data" ^
  -v "%cd%\logs:/app/logs" ^
  jchristn/hnswindex-server:%1
