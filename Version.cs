using System; 
 
namespace Cue2; 
 
/// <summary> 
/// Provides version information for the Cue2 application. 
/// </summary> 
public static class Version 
{ 
    /// <summary> 
    /// Gets the short name of the application. 
    /// </summary> 
    /// <value>The short name string, e.g., "cue2".</value> 
    public static readonly string ShortName = "cue2"; 
 
    /// <summary> 
    /// Gets the full name of the application. 
    /// </summary> 
    /// <value>The full name string, e.g., "Cue2".</value> 
    public static readonly string Name = "Cue2"; 
 
    /// <summary> 
    /// Gets the major version number. 
    /// </summary> 
    /// <value>The major version integer.</value> 
    public static readonly int Major = 0; 
 
    /// <summary> 
    /// Gets the minor version number. 
    /// </summary> 
    /// <value>The minor version integer.</value> 
    public static readonly int Minor = 1; 
 
    /// <summary> 
    /// Gets the patch version number. 
    /// </summary> 
    /// <value>The patch version integer.</value> 
    public static readonly int Patch = 0; 
 
    /// <summary> 
    /// Gets the version status. 
    /// </summary> 
    /// <value>The status string, e.g., "dev".</value> 
    public static readonly string Status = "dev"; 
 
    /// <summary> 
    /// Gets the code name of the version. 
    /// </summary> 
    /// <value>The code name string.</value> 
    public static readonly string CodeName = "StripyHat"; 
 
    /// <summary> 
    /// Gets the module configuration. 
    /// </summary> 
    /// <value>The module config string.</value> 
    public static readonly string ModuleConfig = ""; 
 
    /// <summary> 
    /// Gets the official website URL. 
    /// </summary> 
    /// <value>The website URL string.</value> 
    public static readonly string Website = "https://www.cue2.live/";

    /// <summary>
    /// Gets the documentation website URL.
    /// </summary>
    /// <value>The documentation website URL string.</value>
    public static readonly string DocsWebsite = "https://docs.cue2.live/";
 
    /// <summary>
    /// Gets the documentation version.
    /// </summary>
    /// <value>The docs version string, e.g., "latest".</value>
    public static readonly string Docs = "latest";

    /// <summary>
    /// Gets the full version string combining major, minor, patch, status, and code name.
    /// </summary>
    /// <value>The full version string in the format "v{major}.{minor}.{patch} {status} - {codeName}".</value>
    public static readonly string FullVersionString = $"v{Major}.{Minor}.{Patch} {Status} - {CodeName}";
}
