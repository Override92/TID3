using System;
using System.Globalization;
using System.Threading;
using TID3.Services;
using Xunit;

namespace TID3.Tests
{
    public class TagFormattingTests
    {
        // ---- JoinValues ---------------------------------------------------------

        [Fact]
        public void JoinValues_Null_ReturnsEmpty()
        {
            Assert.Equal("", TagFormatting.JoinValues(null));
        }

        [Fact]
        public void JoinValues_EmptyArray_ReturnsEmpty()
        {
            Assert.Equal("", TagFormatting.JoinValues(Array.Empty<string>()));
        }

        [Fact]
        public void JoinValues_SingleValue_ReturnsItUnchanged()
        {
            Assert.Equal("Radiohead", TagFormatting.JoinValues(new[] { "Radiohead" }));
        }

        [Fact]
        public void JoinValues_MultipleValues_JoinedWithCommaSpace()
        {
            Assert.Equal("Daft Punk, Pharrell Williams",
                TagFormatting.JoinValues(new[] { "Daft Punk", "Pharrell Williams" }));
        }

        // ---- SplitValues --------------------------------------------------------

        [Fact]
        public void SplitValues_Null_ReturnsEmptyArray()
        {
            Assert.Empty(TagFormatting.SplitValues(null));
        }

        [Fact]
        public void SplitValues_Empty_ReturnsEmptyArray()
        {
            Assert.Empty(TagFormatting.SplitValues(""));
        }

        [Fact]
        public void SplitValues_TrimsWhitespaceAroundEachEntry()
        {
            Assert.Equal(new[] { "A", "B", "C" }, TagFormatting.SplitValues("A,  B ,C"));
        }

        [Fact]
        public void SplitValues_SingleValue()
        {
            Assert.Equal(new[] { "Radiohead" }, TagFormatting.SplitValues("Radiohead"));
        }

        [Fact]
        public void JoinThenSplit_RoundTripsMultipleArtists()
        {
            var original = new[] { "Daft Punk", "Pharrell Williams" };
            var joined = TagFormatting.JoinValues(original);

            Assert.Equal(original, TagFormatting.SplitValues(joined));
        }

        // ---- FormatDuration -----------------------------------------------------

        [Theory]
        [InlineData(0, 0, "00:00")]
        [InlineData(0, 45, "00:45")]
        [InlineData(3, 9, "03:09")]
        [InlineData(12, 34, "12:34")]
        public void FormatDuration_Cases(int minutes, int seconds, string expected)
        {
            Assert.Equal(expected, TagFormatting.FormatDuration(new TimeSpan(0, minutes, seconds)));
        }

        // ---- FormatBitrate ------------------------------------------------------

        [Theory]
        [InlineData(320, "320 kbps")]
        [InlineData(128, "128 kbps")]
        [InlineData(0, "Unknown")]
        [InlineData(-1, "Unknown")]
        public void FormatBitrate_Cases(int bitrate, string expected)
        {
            Assert.Equal(expected, TagFormatting.FormatBitrate(bitrate));
        }

        // ---- FormatFileSize -----------------------------------------------------

        [Theory]
        [InlineData(0L, "0.0 B")]
        [InlineData(512L, "512.0 B")]
        [InlineData(1024L, "1.0 KB")]
        [InlineData(1536L, "1.5 KB")]
        [InlineData(5L * 1024 * 1024, "5.0 MB")]
        [InlineData(3L * 1024 * 1024 * 1024, "3.0 GB")]
        public void FormatFileSize_Cases_InvariantCulture(long bytes, string expected)
        {
            // Pin the culture so the decimal separator is predictable for the assertion.
            var previous = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                Assert.Equal(expected, TagFormatting.FormatFileSize(bytes));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Fact]
        public void FormatFileSize_TerabyteRange_DoesNotOverflowSuffixes()
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                // 2 TB — must clamp at the "TB" suffix rather than indexing past it.
                Assert.Equal("2.0 TB", TagFormatting.FormatFileSize(2L * 1024 * 1024 * 1024 * 1024));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Fact]
        public void FormatFileSize_NegativeBytes_TreatedAsZero()
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                Assert.Equal("0.0 B", TagFormatting.FormatFileSize(-100));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }
    }
}
