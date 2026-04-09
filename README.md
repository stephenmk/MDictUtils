Partial port of [mdict-utils](https://github.com/liuyug/mdict-utils)

Oracle testing requires mdict-utils installed. For instance, create a venv at the repo root and run:

```
pip install mdict-utils
```

### Notes
- Because the encoding/decoding depends on the current date, there will be some diff with the commited fixtures if you run this in the future (!). The only thing that matters is that `mdict` and this repo produce the same artifacts _on a given date_.
- Because of differences between python zlib and the c# equivalent, the bytes of the compressed artifact may not exactly match (usually happens after a certain size). This should not matter, the sizes will be approximately equal, and they will decompress to the same data (but oracle test will fail if you compare the compressed artifacts!).

### Links
- [file format doc](https://mdict4j.readthedocs.io/zh-cn/latest/reference/fileformat.html)
- pyglossary has some files about [mdict](https://github.com/ilius/pyglossary/blob/master/pyglossary/plugin_lib/readmdict.py)
- https://github.com/cia1099/mdict
- https://github.com/terasum/js-mdict
- The skeleton of this repo was from [unit testing tutorial](https://docs.microsoft.com/dotnet/core/testing/unit-testing-with-dotnet-test)

### TODO
- [x] Support encoding mdd
  - [ ] Do folders work? Probably not
- [x] Support basic reading operations for debug purposes
  - [x] Support file.mdd -x (passes do-undo test)
  - [x] Clean
  - [x] Write do-undo test into the testsuite
- [x] Support file.mdx -x
  - [x] Write do-undo test into the testsuite
  - [x] Clean
- [x] Support -m command
- [x] Support passing html as title/description
- [ ] Add LICENCE
- [ ] Remove all non-version-2.0 branches because noise
- [ ] Release binaries (?)
