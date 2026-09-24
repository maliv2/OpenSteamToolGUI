using System.Text.Json;

namespace OpenSteamToolGUI.Core;

public static class SteamArtwork
{
    public static IReadOnlyList<Uri> StoreAssetUrls(uint appId, JsonElement root)
    {
        if (!root.TryGetProperty(appId.ToString(), out var result) ||
            !result.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True ||
            !result.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return [];

        var urls = new List<Uri>();
        foreach (string field in new[] { "header_image", "capsule_image" })
        {
            if (!data.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String ||
                !Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                uri.Host is not ("shared.akamai.steamstatic.com" or "shared.cloudflare.steamstatic.com" or "shared.fastly.steamstatic.com") ||
                !uri.AbsolutePath.StartsWith($"/store_item_assets/steam/apps/{appId}/", StringComparison.Ordinal) ||
                !new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(Path.GetExtension(uri.AbsolutePath), StringComparer.OrdinalIgnoreCase))
                continue;
            urls.Add(uri);
        }
        return urls;
    }
}
