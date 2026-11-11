namespace KeyboardUtils.Tests;

public class SpeedRampTests
{
    [Theory]
    [InlineData(50, 89.9, 50)]   // not good enough: same speed
    [InlineData(50, 90, 55)]     // good: a step faster
    [InlineData(75, 100, 80)]
    [InlineData(98, 95, 100)]    // never past full speed
    [InlineData(100, 100, 100)]
    [InlineData(25, 0, 25)]
    public void Speeds_up_after_a_good_pass(int speed, double passPercent, int expected)
    {
        Assert.Equal(expected, SpeedRamp.Next(speed, passPercent));
    }

    [Fact]
    public void Good_passes_bring_a_loop_from_half_to_full_speed()
    {
        int speed = 50, passes = 0;
        while (speed < 100)
        {
            speed = SpeedRamp.Next(speed, 95);
            passes++;
        }
        Assert.Equal(10, passes);
    }
}
