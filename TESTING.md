# Testing

Commands are shown for `cmd.exe` from the repository root.
Storage overrides run the full shared suite against the selected backend.

## Default

Runs the normal test matrix with the default storage coverage.

```cmd
dotnet test .\src\HnswLite.sln
dotnet run --framework net8.0 --project .\src\Test.Automated\Test.Automated.csproj
```

## SQLite Override

Use the console runner CLI when you want an explicit SQLite database file.

```cmd
dotnet run --framework net8.0 --project .\src\Test.Automated\Test.Automated.csproj -- --storage sqlite --filename test.db
```

Use environment variables for xUnit, NUnit, and MSTest.

```cmd
set HNSWLITE_TEST_STORAGE=sqlite
set HNSWLITE_TEST_SQLITE_FILENAME=test.db

dotnet test .\src\Test.XUnit\Test.XUnit.csproj
dotnet test .\src\Test.NUnit\Test.NUnit.csproj
dotnet test .\src\Test.MSTest\Test.MSTest.csproj
```

## PostgreSQL Override

Start the Docker PostgreSQL service if you do not already have a test database.

```cmd
docker compose -f .\docker\compose.yaml up -d hnswlite-postgres hnswlite-postgres-provisioner
```

Run the console runner against PostgreSQL.

```cmd
dotnet run --framework net8.0 --project .\src\Test.Automated\Test.Automated.csproj -- --storage postgresql --host localhost --user hnswlite --pass hnswlite --schema public --databasename hnswlite
```

Use environment variables for xUnit, NUnit, and MSTest.

```cmd
set HNSWLITE_TEST_STORAGE=postgresql
set HNSWLITE_TEST_POSTGRES_HOST=localhost
set HNSWLITE_TEST_POSTGRES_USER=hnswlite
set HNSWLITE_TEST_POSTGRES_PASSWORD=hnswlite
set HNSWLITE_TEST_POSTGRES_DATABASE=hnswlite
set HNSWLITE_TEST_POSTGRES_SCHEMA=public

dotnet test .\src\Test.XUnit\Test.XUnit.csproj
dotnet test .\src\Test.NUnit\Test.NUnit.csproj
dotnet test .\src\Test.MSTest\Test.MSTest.csproj
```
