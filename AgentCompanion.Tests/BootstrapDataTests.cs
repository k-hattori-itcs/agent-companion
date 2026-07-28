using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace AgentCompanion.Tests;

public sealed class BootstrapDataTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "AgentCompanion.Tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void ExtractEmbeddedPets_PreservesExistingCharacterFiles()
    {
        var petsDirectory = Path.Combine(_root, "pets");
        var koharuDirectory = Path.Combine(petsDirectory, "koharu");
        Directory.CreateDirectory(koharuDirectory);
        var existingManifest = Path.Combine(koharuDirectory, "pet.json");
        File.WriteAllText(existingManifest, "user customized");

        App.ExtractEmbeddedPets(Assembly.GetAssembly(typeof(App))!, petsDirectory);

        Assert.Equal("user customized", File.ReadAllText(existingManifest));
        Assert.True(File.Exists(Path.Combine(koharuDirectory, "spritesheet.webp")));
        Assert.True(File.Exists(Path.Combine(petsDirectory, "luna", "pet.json")));
        Assert.True(File.Exists(Path.Combine(petsDirectory, "natsuki", "pet.json")));
        Assert.True(File.Exists(Path.Combine(petsDirectory, "natsuki", "spritesheet.png")));

        var manager = new AgentCompanion.Services.PetManager(petsDirectory);
        manager.Setup();
        var natsuki = Assert.Single(manager.Pets, pet => pet.Id == "natsuki");
        Assert.Equal("Natsuki", natsuki.DisplayName);
        Assert.Equal("spritesheet.png", natsuki.SpritesheetPath);
        Assert.Equal(2, natsuki.SpriteVersionNumber);
        var layout = Assert.IsType<AgentCompanion.Models.PetSpritesheetLayout>(natsuki.SpritesheetLayout);
        Assert.Equal(8, layout.Columns);
        Assert.Equal(11, layout.Rows);
        Assert.Equal(192, layout.CellWidth);
        Assert.Equal(208, layout.CellHeight);
        Assert.Equal(16, layout.LookDirectionCount);
        Assert.NotNull(layout.NeutralLookFrame);
        Assert.Equal(0, layout.NeutralLookFrame!.RowIndex);
        Assert.Equal(0, layout.NeutralLookFrame.ColumnIndex);
        var dimensions = AgentCompanion.Services.SpriteLoader.ValidateImage(
            Path.Combine(natsuki.Directory, natsuki.SpritesheetPath));
        Assert.Equal(1536, dimensions.Width);
        Assert.Equal(2288, dimensions.Height);
    }

    [Fact]
    public void ExtractEmbeddedPets_UpdatesUnmodifiedBundledFile()
    {
        var petsDirectory = Path.Combine(_root, "pets");
        var assembly = Assembly.GetAssembly(typeof(App))!;
        App.ExtractEmbeddedPets(assembly, petsDirectory);

        var manifestPath = Path.Combine(petsDirectory, "natsuki", "pet.json");
        const string previousBundledContent = "{\"id\":\"natsuki\",\"displayName\":\"Old Natsuki\"}";
        File.WriteAllText(manifestPath, previousBundledContent);
        WriteBundledHashes(
            petsDirectory,
            "natsuki/pet.json",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(previousBundledContent))));

        App.ExtractEmbeddedPets(assembly, petsDirectory);

        Assert.DoesNotContain("Old Natsuki", File.ReadAllText(manifestPath));
    }

    [Fact]
    public void ExtractEmbeddedPets_PreservesFileChangedAfterBundledExtraction()
    {
        var petsDirectory = Path.Combine(_root, "pets");
        var assembly = Assembly.GetAssembly(typeof(App))!;
        App.ExtractEmbeddedPets(assembly, petsDirectory);

        var manifestPath = Path.Combine(petsDirectory, "natsuki", "pet.json");
        File.WriteAllText(manifestPath, "user customized after extraction");

        App.ExtractEmbeddedPets(assembly, petsDirectory);

        Assert.Equal("user customized after extraction", File.ReadAllText(manifestPath));
    }

    private static void WriteBundledHashes(string petsDirectory, string relativePath, string hash)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            [relativePath] = hash
        });
        File.WriteAllText(Path.Combine(petsDirectory, ".bundled-assets.json"), json);
    }
}
