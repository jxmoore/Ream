using Ream.Core.Models;

namespace Ream.Tests;

public class NoteContentTests
{
    private const string Sample = """
        <ReamNote schemaVersion="1" id="6c1f4d2e-0000-4000-8000-000000000001">
          <Doc>
            <P><R>First</R><R b="1"> line</R></P>
            <P><R>two</R><BR /><R>lines</R></P>
            <UL><LI><P><R>bullet</R></P></LI></UL>
            <P><IMG src="asset://a.png" /></P>
          </Doc>
        </ReamNote>
        """;

    [Fact]
    public void ToPlainText_JoinsRunsParagraphsBreaksAndListItems()
    {
        Assert.Equal("First line\ntwo\nlines\nbullet\n", NoteContent.ToPlainText(Sample) + "\n");
        Assert.DoesNotContain("asset", NoteContent.ToPlainText(Sample));
    }

    [Fact]
    public void ToPlainText_PassesThroughTextThatIsNotANote()
    {
        Assert.Equal("just text\nhere", NoteContent.ToPlainText("just text\nhere"));
    }

    [Fact]
    public void ToPlainText_PassesThroughMalformedNotes()
    {
        const string broken = "<ReamNote><Doc><P>";
        Assert.Equal(broken, NoteContent.ToPlainText(broken));
    }

    [Fact]
    public void TryParse_RefusesDocumentTypeDeclarations()
    {
        const string hostile = """<?xml version="1.0"?><!DOCTYPE ReamNote [<!ENTITY a "b">]><ReamNote><Doc /></ReamNote>""";
        Assert.False(NoteContent.TryParse(hostile, out _));
    }

    [Theory]
    [InlineData("<ReamNote />", true)]
    [InlineData("  <?xml version=\"1.0\"?><ReamNote />", true)]
    [InlineData("plain text", false)]
    [InlineData("", false)]
    public void IsReamNote_RecognisesTheFormat(string content, bool expected)
    {
        Assert.Equal(expected, NoteContent.IsReamNote(content));
    }

    [Fact]
    public void DeriveTitle_UsesFirstNonEmptyLineAndCapsLength()
    {
        Assert.Equal("Hello", NoteContent.DeriveTitle("\n  \n Hello \nworld"));
        Assert.Equal("Untitled", NoteContent.DeriveTitle("  \n \n"));
        Assert.Equal(60, NoteContent.DeriveTitle(new string('x', 200)).Length);
        Assert.Null(NoteContent.TryDeriveTitle(""));
    }
}
