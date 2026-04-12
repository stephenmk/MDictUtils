[![Build status](https://github.com/daxida/MDictUtils/workflows/ci/badge.svg)](https://github.com/daxida/MDictUtils/actions)

Partial port of [mdict-utils](https://github.com/liuyug/mdict-utils). Tested in Linux.

The main purpose of this port is to support the mdict version of [Jitendex](https://github.com/Jitendex/Jitendex) and as such, it only contains a subset of the original mdict-utils functionality. Namely, it does no encrypting and assumes the version to be 2.0.

### Testing

Oracle testing requires mdict-utils installed. For instance, create a venv at the repo root and run:

```
pip install mdict-utils
```

- Because the encoding/decoding depends on the current date, there will be some diff with the commited fixtures if you run this in the future (!). The only thing that matters is that `mdict` and this repo produce the same artifacts _on a given date_.
- Because of differences between python zlib and the c# equivalent, the bytes of the compressed artifact may not exactly match (usually happens after a certain size). This should not matter, the sizes will be approximately equal, and they will decompress to the same data (but oracle test will fail if you compare the compressed artifacts!).

### Links
- [spec](https://mdict4j.readthedocs.io/zh-cn/latest/reference/fileformat.html)
- A precursor of mdict-utils: [mdict-analysis](https://github.com/csarron/mdict-analysis)
- pyglossary has some files about [mdict](https://github.com/ilius/pyglossary/blob/master/pyglossary/plugin_lib/readmdict.py)
- https://github.com/cia1099/mdict
- https://github.com/terasum/js-mdict
- The skeleton of this repo was from [unit testing tutorial](https://docs.microsoft.com/dotnet/core/testing/unit-testing-with-dotnet-test)

### TODO
- [ ] Remove all non-version-2.0 branches because noise
- [ ] Do decryption (?)
- [ ] Release binaries (?)
- [ ] Explore how to use as a library (writing a glossary from memory instead of disk)

### Others
- [ ] How good is [pyglossary](https://github.com/ilius/pyglossary) for wty/Jitendex? Test in goldendict
- [ ] Stardict from mdict artifacts for Jitendex?
- [ ] How hard is it to write some code that takes a Yomitan structured content thingy and returns html at home
