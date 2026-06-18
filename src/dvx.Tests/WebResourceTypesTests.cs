using dvx.Services;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class WebResourceTypesTests
    {
        [Theory]
        [InlineData("a.js", 3)]
        [InlineData("a.JS", 3)]          // case-insensitive
        [InlineData("style.css", 2)]
        [InlineData("page.html", 1)]
        [InlineData("page.htm", 1)]
        [InlineData("data.xml", 4)]
        [InlineData("logo.png", 5)]
        [InlineData("photo.jpg", 6)]
        [InlineData("photo.jpeg", 6)]
        [InlineData("anim.gif", 7)]
        [InlineData("app.xap", 8)]
        [InlineData("t.xsl", 9)]
        [InlineData("t.xslt", 9)]
        [InlineData("fav.ico", 10)]
        [InlineData("icon.svg", 11)]
        [InlineData("strings.resx", 12)]
        public void TryInferType_KnownExtensions(string file, int expected)
        {
            WebResourceTypes.TryInferType(file, out var type).ShouldBeTrue();
            type.ShouldBe(expected);
        }

        [Theory]
        [InlineData("notes.txt")]
        [InlineData("data.json")]
        [InlineData("noext")]
        public void TryInferType_UnknownExtension_ReturnsFalse(string file)
        {
            WebResourceTypes.TryInferType(file, out var type).ShouldBeFalse();
            type.ShouldBe(0);
        }

        [Theory]
        [InlineData(1, true)]    // html
        [InlineData(2, true)]    // css
        [InlineData(3, true)]    // js
        [InlineData(4, true)]    // xml
        [InlineData(9, true)]    // xsl
        [InlineData(11, true)]   // svg
        [InlineData(12, true)]   // resx
        [InlineData(5, false)]   // png
        [InlineData(6, false)]   // jpg
        [InlineData(7, false)]   // gif
        [InlineData(8, false)]   // xap
        [InlineData(10, false)]  // ico
        public void IsText_Classifies(int type, bool isText)
            => WebResourceTypes.IsText(type).ShouldBe(isText);
    }
}
