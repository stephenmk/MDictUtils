run:
  dotnet run --project Cli -- assets/out2.mdx -a assets/stub.txt

oracle:
  mdict assets/out1.mdx -a assets/stub.txt

test:
  dotnet test Lib.Tests/

final:
  @just run
  @just oracle
  cmp assets/out1.mdx assets/out2.mdx

alias t := test
alias r := run
