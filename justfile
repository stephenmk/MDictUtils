# Run the (dev) command line
cli *args:
  dotnet run --project Cli -- {{args}}

# Run benchmarks
bench:
  dotnet run -c Release --project Lib.Benchmark

# Run unit tests
test *args:
  dotnet test Lib.Tests/ {{args}}

run:
  dotnet run --project Cli -- assets/out2.mdx -a assets/stub.txt -a assets/extra.txt --title assets/title.html --description assets/description.html
  dotnet run --project Cli -- assets/out2.mdd -a assets/stub.txt

oracle:
  mdict assets/out1.mdx -a assets/stub.txt --title assets/title.html -a assets/extra.txt --description assets/description.html
  mdict assets/out1.mdd -a assets/stub.txt

do-undo:
  dotnet run --project Cli -- assets/out1.mdd -a assets/stub.txt && \
  dotnet run --project Cli -- assets/out1.mdd -x
  diff --strip-trailing-cr stub.txt assets/stub.txt
  rm stub.txt

oracle-do-undo:
  mdict assets/out1.mdx -a assets/stub.txt && \
  mdict -x assets/out1.mdx
  diff --strip-trailing-cr out1.mdx.txt assets/stub.txt

cmp *args:
  # Comparing meta will fail because python prints the timer at the end
  cmp {{args}} assets/out1.mdx assets/out2.mdx
  cmp {{args}} assets/out1.mdd assets/out2.mdd

final:
  @just run
  @just oracle
  just cmp
  @echo ALL GOOD

# Otherwise nvim go to definition sends you to assembly and not source code
sln:
  dotnet new sln -n MDictUtils --force
  dotnet sln MDictUtils.slnx add Cli/Cli.csproj
  dotnet sln MDictUtils.slnx add Lib/Lib.csproj
  dotnet sln MDictUtils.slnx add Lib.Tests/Lib.Tests.csproj
  dotnet sln MDictUtils.slnx add Lib.Benchmark/Lib.Benchmark.csproj
  dotnet build MDictUtils.slnx

alias r := run
alias t := test
