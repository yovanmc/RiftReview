using RiftReview.App.Controls;
using Xunit;

public class LineChartTests
{
    [Fact]
    public void Scaler_maps_data_bounds_to_pixel_rect()
    {
        var sc = new ChartScaler(minX: 0, maxX: 10, minY: -100, maxY: 100, width: 200, height: 100, pad: 0);
        Assert.Equal(0, sc.X(0), 3);
        Assert.Equal(200, sc.X(10), 3);
        Assert.Equal(100, sc.Y(-100), 3); // bottom
        Assert.Equal(0, sc.Y(100), 3);    // top
        Assert.Equal(50, sc.Y(0), 3);     // zero line mid
    }
}
