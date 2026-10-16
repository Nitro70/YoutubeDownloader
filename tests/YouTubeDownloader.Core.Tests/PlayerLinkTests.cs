using Xunit;
using YouTubeDownloader.Core;

namespace YouTubeDownloader.Core.Tests;

public class PlayerLinkTests
{
    private const string Page = "https://nitro70.github.io/YoutubeDownloader/";

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=jNQXAC9IVRw", Page + "?v=jNQXAC9IVRw")]
    [InlineData("https://youtu.be/jNQXAC9IVRw", Page + "?v=jNQXAC9IVRw")]
    [InlineData("youtu.be/jNQXAC9IVRw", Page + "?v=jNQXAC9IVRw")]
    [InlineData("https://m.youtube.com/watch?v=jNQXAC9IVRw", Page + "?v=jNQXAC9IVRw")]
    [InlineData("https://www.youtube.com/shorts/jNQXAC9IVRw", Page + "?v=jNQXAC9IVRw")]
    [InlineData("https://www.youtube.com/embed/jNQXAC9IVRw", Page + "?v=jNQXAC9IVRw")]
    [InlineData("https://www.youtube.com/live/jNQXAC9IVRw", Page + "?v=jNQXAC9IVRw")]
    [InlineData("https://www.youtube.com/watch?v=jNQXAC9IVRw&list=PL1234567890", Page + "?v=jNQXAC9IVRw")]
    [InlineData("https://youtu.be/jNQXAC9IVRw?t=42", Page + "?v=jNQXAC9IVRw&t=42")]
    [InlineData("https://youtu.be/jNQXAC9IVRw?t=90s", Page + "?v=jNQXAC9IVRw&t=90")]
    [InlineData("https://www.youtube.com/watch?v=jNQXAC9IVRw&t=1m30s", Page + "?v=jNQXAC9IVRw&t=90")]
    [InlineData("https://www.youtube.com/watch?v=jNQXAC9IVRw&t=1h2m3s", Page + "?v=jNQXAC9IVRw&t=3723")]
    [InlineData("https://www.youtube.com/embed/jNQXAC9IVRw?start=15", Page + "?v=jNQXAC9IVRw&t=15")]
    [InlineData("https://youtu.be/jNQXAC9IVRw?t=0", Page + "?v=jNQXAC9IVRw")]
    public void Builds_the_player_link(string input, string expected)
    {
        Assert.True(PlayerLink.TryCreate(input, out string link));
        Assert.Equal(expected, link);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("me at the zoo")]
    [InlineData("https://www.youtube.com/playlist?list=PL1234567890")]
    [InlineData("https://www.youtube.com/@jawed")]
    [InlineData("https://vimeo.com/123456")]
    public void Refuses_anything_but_a_video_link(string? input)
    {
        Assert.False(PlayerLink.TryCreate(input, out string link));
        Assert.Equal(string.Empty, link);
    }
}
