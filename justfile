cli *args:
  dotnet run --project Cli -- {{args}}

run:
  dotnet run --project Cli -- assets/out2.mdx -a assets/stub.txt -a assets/extra.txt --title assets/title.html --description assets/description.html
  dotnet run --project Cli -- assets/out2.mdd -a assets/stub.txt
  # dotnet run --project Cli -- assets/out2.mdx -m > assets/meta2

oracle:
  mdict assets/out1.mdx -a assets/stub.txt --title assets/title.html -a assets/extra.txt --description assets/description.html
  mdict assets/out1.mdd -a assets/stub.txt
  # mdict assets/out1.mdx -m > assets/meta1

do-undo:
  dotnet run --project Cli -- assets/out1.mdd -a assets/stub.txt && \
  dotnet run --project Cli -- assets/out1.mdd -x
  diff --strip-trailing-cr stub.txt assets/stub.txt
  rm stub.txt

oracle-do-undo:
  mdict assets/out1.mdx -a assets/stub.txt && \
  mdict -x assets/out1.mdx
  diff --strip-trailing-cr out1.mdx.txt assets/stub.txt

test:
  dotnet test Lib.Tests/

final-old:
  dotnet run --project Cli -- assets/out2.mdx -a assets/stub.txt
  mdict assets/out1.mdx -a assets/stub.txt
  cmp assets/out1.mdx assets/out2.mdx

cmp *args:
  cmp {{args}} assets/out1.mdx assets/out2.mdx
  cmp {{args}} assets/out1.mdd assets/out2.mdd

final:
  @just run
  @just oracle
  just cmp
  # Comparing meta will fail because python prints the timer at the end
  # cmp assets/meta1 assets/meta2

# Otherwise nvim go to definition sends you to assembly and not source code
sln:
  dotnet new sln -n MDictUtils --force
  dotnet sln MDictUtils.slnx add Cli/Cli.csproj
  dotnet sln MDictUtils.slnx add Lib/Lib.csproj
  dotnet sln MDictUtils.slnx add Lib.Tests/Lib.Tests.csproj
  dotnet build MDictUtils.slnx

alias r := run
alias t := test
