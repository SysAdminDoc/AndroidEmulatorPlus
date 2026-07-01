using System.Text.Json;
using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class RecipeServiceTests
{
    [Fact]
    public void Recipe_round_trips_through_json()
    {
        var recipe = new Recipe
        {
            Name = "test-setup",
            Description = "Test setup recipe",
            CreatedUtc = "2026-07-01T00:00:00Z",
            Steps =
            {
                new RecipeStep { Action = "launch", Args = new() { ["avd"] = "Pixel_7" }, Description = "Launch AVD" },
                new RecipeStep { Action = "wait", Args = new() { ["seconds"] = "5" }, Description = "Wait 5s" },
                new RecipeStep { Action = "install", Args = new() { ["path"] = "test.apk" }, Description = "Install" },
                new RecipeStep { Action = "shell", Args = new() { ["command"] = "pm list packages" }, Description = "List" },
            },
        };

        var json = JsonSerializer.Serialize(recipe, new JsonSerializerOptions { WriteIndented = true });
        var deserialized = JsonSerializer.Deserialize<Recipe>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("test-setup", deserialized!.Name);
        Assert.Equal(4, deserialized.Steps.Count);
        Assert.Equal("launch", deserialized.Steps[0].Action);
        Assert.Equal("Pixel_7", deserialized.Steps[0].Args["avd"]);
    }

    [Fact]
    public void RecipeStep_supports_all_known_actions()
    {
        var knownActions = new[] { "launch", "install", "push", "shell", "console", "uninstall", "wait" };
        foreach (var action in knownActions)
        {
            var step = new RecipeStep { Action = action };
            Assert.Equal(action, step.Action);
        }
    }
}
