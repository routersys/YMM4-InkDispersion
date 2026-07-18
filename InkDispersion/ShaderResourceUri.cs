namespace InkDispersion;

internal static class ShaderResourceUri
{
    public static Uri Get(string shaderName) => new($"pack://application:,,,/InkDispersion;component/Shaders/{shaderName}.cso", UriKind.Absolute);
}
