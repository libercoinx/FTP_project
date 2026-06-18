using FtpClient.Infrastructure.Ftp;

namespace FtpClient.Tests;

public sealed class FtpListParserTests
{
    [Fact]
    public void ParseMlsd_ReadsDirectoryAndUnicodeFile()
    {
        const string input =
            "type=dir;modify=20260102120000;perm=el; documents\r\n" +
            "type=file;size=128;modify=20260103140506;perm=adfrw; 中文 文件.txt\r\n";

        var entries = FtpListParser.ParseMlsd(input, "/home");

        Assert.Equal(2, entries.Count);
        Assert.True(entries[0].IsDirectory);
        Assert.Equal("/home/documents", entries[0].FullPath);
        Assert.False(entries[1].IsDirectory);
        Assert.Equal("中文 文件.txt", entries[1].Name);
        Assert.Equal(128, entries[1].Size);
    }

    [Fact]
    public void ParseUnixList_PreservesSpacesInName()
    {
        const string input = "-rw-r--r-- 1 owner group 4096 Jun 18 13:20 report final.pdf\r\n";

        var entry = Assert.Single(FtpListParser.ParseUnixList(input, "/"));

        Assert.Equal("report final.pdf", entry.Name);
        Assert.Equal("/report final.pdf", entry.FullPath);
        Assert.Equal(4096, entry.Size);
    }

    [Theory]
    [InlineData("/", "file.txt", "/file.txt")]
    [InlineData("/folder", "file.txt", "/folder/file.txt")]
    [InlineData("/folder/", "file.txt", "/folder/file.txt")]
    public void CombineRemotePath_NormalizesSeparator(string directory, string name, string expected)
    {
        Assert.Equal(expected, FtpListParser.CombineRemotePath(directory, name));
    }
}
