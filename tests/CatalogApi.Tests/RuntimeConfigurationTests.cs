using CatalogApi.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CatalogApi.Tests;

public class RuntimeConfigurationTests
{
    [Fact]
    public void Mounted_secret_files_override_existing_settings()
    {
        var dir = Directory.CreateTempSubdirectory("fcg-secrets-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "Jwt__SecretKey"), "from-key-vault");
            File.WriteAllText(Path.Combine(dir.FullName, "ConnectionStrings__Postgres"), "Host=db;Password=vault");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:SecretKey"] = "placeholder" })
                .AddMountedSecrets(dir.FullName)
                .Build();

            config["Jwt:SecretKey"].Should().Be("from-key-vault");
            config.GetConnectionString("Postgres").Should().Be("Host=db;Password=vault");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Without_a_secrets_path_configuration_is_unchanged(string? path)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:SecretKey"] = "placeholder" })
            .AddMountedSecrets(path)
            .Build();

        config["Jwt:SecretKey"].Should().Be("placeholder");
    }

    [Fact]
    public void Missing_secrets_directory_fails_fast()
    {
        var act = () => new ConfigurationBuilder().AddMountedSecrets(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Responses_carry_the_deployed_version()
    {
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        app.UseAppVersionHeader();
        app.Run(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();

        await app.Build()(context);

        context.Response.Headers[RuntimeConfiguration.VersionHeader].ToString().Should().Be(RuntimeConfiguration.AppVersion);
    }
}
