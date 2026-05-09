using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

// fast implementation for loading wgsl shader in runtime
// It also implements a rough/simple include logic
// I didn't find any library to do this in C#, probably worth creating one?
public static class ShaderLoader
{
    public static string LoadEmbedded(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        
        string fullName = resourceName.EndsWith(".wgsl") ? 
            resourceName : $"{resourceName}.wgsl";
        
        fullName = $"{assembly.GetName().Name}.Shaders.{fullName.Replace("/",".")}";
        
        using var stream = assembly.GetManifestResourceStream(fullName);
        if (stream == null)
        {
            var availableResources = assembly.GetManifestResourceNames();
            throw new Exception(
                $"Embedded shader not found: {fullName}\n" +
                $"Available resources:\n  {string.Join("\n  ", availableResources)}"
            );
        }
        
        using var reader = new StreamReader(stream);
        string shaderCode = reader.ReadToEnd();
        
        return ProcessIncludes(shaderCode);
    }
    
    private static string ProcessIncludes(string code)
    {
        var includePattern = @"//\s*#include\s+""([^""]+)""";
        
        return Regex.Replace(code, includePattern, match =>
        {
            string includeFile = match.Groups[1].Value;
            string includeCode = LoadEmbedded(includeFile);
            return $"// BEGIN INCLUDE: {includeFile}\n{includeCode}\n// END INCLUDE: {includeFile}";
        });
    }
}