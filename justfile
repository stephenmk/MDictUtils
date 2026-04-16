# Run the (dev) command line
cli *args:
  dotnet run --project MDictUtils.Cli -- {{args}}

# Run benchmarks
bench:
  dotnet run -c Release --project MDictUtils.Benchmark

# Run unit tests
test *args:
  dotnet test MDictUtils.Tests/ {{args}}

# Format code
fmt:
  dotnet format --verbosity normal

# Run the code against the fixtures
run *args:
  dotnet build MDictUtils.Cli -c Release
  dotnet MDictUtils.Cli/bin/Release/net*/MDictUtils.Cli.dll assets/out2.mdx -a assets/stub.txt -a assets/extra.txt --title assets/title.html --description assets/description.html {{args}}
  dotnet MDictUtils.Cli/bin/Release/net*/MDictUtils.Cli.dll assets/out2.mdd -a assets/stub.txt {{args}}

oracle:
  mdict assets/out1.mdx -a assets/stub.txt --title assets/title.html -a assets/extra.txt --description assets/description.html
  mdict assets/out1.mdd -a assets/stub.txt

do-undo:
  dotnet build MDictUtils.Cli -c Release
  dotnet MDictUtils.Cli/bin/Release/net*/MDictUtils.Cli.dll assets/out1.mdd -a assets/stub.txt && \
  dotnet MDictUtils.Cli/bin/Release/net*/MDictUtils.Cli.dll assets/out1.mdd -x
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
  dotnet sln MDictUtils.slnx add MDictUtils.Cli/MDictUtils.Cli.csproj
  dotnet sln MDictUtils.slnx add MDictUtils/MDictUtils.csproj
  dotnet sln MDictUtils.slnx add MDictUtils.Tests/MDictUtils.Tests.csproj
  dotnet sln MDictUtils.slnx add MDictUtils.Benchmark/MDictUtils.Benchmark.csproj
  dotnet build MDictUtils.slnx

# Re-build jitendex from a downloaded, unzipped dict at .tmp/ folder
# Download it at: https://jitendex.org/pages/downloads.html
jitendex:
  mkdir -p build2/out
  dotnet build MDictUtils.Cli -c Release
  # Extract txt/media files into build2 folder
  dotnet MDictUtils.Cli/bin/Release/net*/MDictUtils.Cli.dll -x .tmp/jitendex-mdict/jitendex/jitendex.mdx -d build2
  dotnet MDictUtils.Cli/bin/Release/net*/MDictUtils.Cli.dll -x .tmp/jitendex-mdict/jitendex/jitendex.mdd -d build2/media
  # Re-build the dict from build2 folder
  dotnet MDictUtils.Cli/bin/Release/net*/MDictUtils.Cli.dll \
      -a build2/jitendex.mdx.txt \
      --title build2/jitendex.mdx.title.html \
      --description build2/jitendex.mdx.description.html \
      build2/out/jitendex.mdx
  dotnet MDictUtils.Cli/bin/Release/net*/MDictUtils.Cli.dll \
      -a build2/media \
      --title build2/jitendex.mdx.title.html \
      --description build2/jitendex.mdx.description.html \
      build2/out/jitendex.mdd
  # Move the other original files so we have a full dict at build2 folder
  cp .tmp/jitendex-mdict/jitendex/common.css build2/out/common.css
  cp .tmp/jitendex-mdict/jitendex/jitendex.css build2/out/jitendex.css
  cp .tmp/jitendex-mdict/jitendex/jitendex.png build2/out/jitendex.png

# Same as jitendex but with mdict
jitendex-py:
  mkdir -p build1/out
  mdict -x .tmp/jitendex-mdict/jitendex/jitendex.mdx -d build1
  mdict -x .tmp/jitendex-mdict/jitendex/jitendex.mdd -d build1/media
  mdict \
      -a build1/jitendex.mdx.txt \
      --title build1/jitendex.mdx.title.html \
      --description build1/jitendex.mdx.description.html \
      build1/out/jitendex.mdx
  mdict \
      -a build1/media \
      --title build1/jitendex.mdx.title.html \
      --description build1/jitendex.mdx.description.html \
      build1/out/jitendex.mdd
  # Move the other original files so we have a full dict at build1 folder
  cp .tmp/jitendex-mdict/jitendex/common.css build1/out/common.css
  cp .tmp/jitendex-mdict/jitendex/jitendex.css build1/out/jitendex.css
  cp .tmp/jitendex-mdict/jitendex/jitendex.png build1/out/jitendex.png


alias b := bench
alias r := run
alias t := test
