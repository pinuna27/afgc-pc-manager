using AFGCPCManager.Core.Mapping;

namespace AFGCPCManager.Core.Tests.Mapping;

public sealed class EffectiveMappingResolverTests
{
    [Fact]
    public void UntouchedFieldsInheritDefaults()
    {
        ControllerMappingProfile defaults = new()
        {
            HomeButton = HomeButtonMode.Original,
            MediaRow = MediaRowMode.Navigation
        };
        ControllerMappingOverrides overrides = new()
        {
            MediaRow = MediaRowMode.Disabled
        };

        ControllerMappingProfile resolved =
            EffectiveMappingResolver.Resolve(defaults, overrides);

        Assert.Equal(HomeButtonMode.Original, resolved.HomeButton);
        Assert.Equal(MediaRowMode.Disabled, resolved.MediaRow);
    }

    [Theory]
    [InlineData(HomeButtonMode.Original)]
    [InlineData(HomeButtonMode.Disabled)]
    public void NonGuideHomeForcesGameCircleToGuide(HomeButtonMode home)
    {
        ControllerMappingProfile resolved = EffectiveMappingResolver.Resolve(
            new ControllerMappingProfile { HomeButton = home },
            new ControllerMappingOverrides
            {
                GameCircleButton = GameCircleButtonMode.Disabled
            });

        Assert.Equal(GameCircleButtonMode.Guide, resolved.GameCircleButton);
    }
}
