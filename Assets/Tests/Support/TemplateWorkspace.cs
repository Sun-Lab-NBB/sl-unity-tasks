/// <summary>
/// Provides the TemplateWorkspace helper that stages a throwaway task-template directory tree on disk.
///
/// ConfigLoader resolves a cue texture relative to the template file as "&lt;template directory&gt;/../Textures", so a
/// test that exercises texture validation needs that exact two-directory shape rather than a flat temporary folder.
/// </summary>
using System;
using System.IO;

namespace SL.Tests
{
    /// <summary>
    /// Stages a temporary Configurations and Textures directory pair and removes it when the test completes.
    /// </summary>
    public sealed class TemplateWorkspace : IDisposable
    {
        /// <summary>The name of the directory holding the staged template YAML files.</summary>
        private const string ConfigurationsDirectoryName = "Configurations";

        /// <summary>The name of the sibling directory holding the staged cue texture files.</summary>
        private const string TexturesDirectoryName = "Textures";

        /// <summary>The root directory containing the Configurations and Textures pair.</summary>
        private string RootPath { get; }

        /// <summary>The directory that staged template YAML files are written into.</summary>
        private string ConfigurationsPath { get; }

        /// <summary>The directory that staged cue texture files are written into.</summary>
        private string TexturesPath { get; }

        /// <summary>Creates the workspace directory tree under the system temporary directory.</summary>
        private TemplateWorkspace()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "sollertia-vr-tests", Guid.NewGuid().ToString("N"));
            ConfigurationsPath = Path.Combine(RootPath, ConfigurationsDirectoryName);
            TexturesPath = Path.Combine(RootPath, TexturesDirectoryName);
            Directory.CreateDirectory(ConfigurationsPath);
            Directory.CreateDirectory(TexturesPath);
        }

        /// <summary>Creates a workspace whose directory tree is ready for template and texture writes.</summary>
        /// <returns>The workspace, which the caller disposes to delete the staged tree.</returns>
        public static TemplateWorkspace Create()
        {
            return new TemplateWorkspace();
        }

        /// <summary>Writes a template YAML file named after the template and returns its absolute path.</summary>
        /// <param name="templateName">The template name, which becomes the file name without its extension.</param>
        /// <param name="yamlText">The YAML document body written verbatim.</param>
        /// <returns>The absolute path of the written template file.</returns>
        public string WriteTemplate(string templateName, string yamlText)
        {
            string path = TemplatePath(templateName);
            File.WriteAllText(path, yamlText);
            return path;
        }

        /// <summary>Writes a template built by the supplied builder along with every cue texture it names.</summary>
        /// <param name="templateName">The template name, which becomes the file name without its extension.</param>
        /// <param name="template">The builder whose YAML body and cue texture names are staged.</param>
        /// <returns>The absolute path of the written template file.</returns>
        public string WriteTemplate(string templateName, TemplateYaml template)
        {
            foreach (string textureName in template.ReferencedTextureNames())
            {
                WriteTexture(textureName);
            }
            return WriteTemplate(templateName, template.Build());
        }

        /// <summary>Returns the absolute path a template of the given name occupies, written or not.</summary>
        /// <param name="templateName">The template name, which becomes the file name without its extension.</param>
        /// <returns>The absolute path of the template file.</returns>
        public string TemplatePath(string templateName)
        {
            return Path.Combine(ConfigurationsPath, $"{templateName}.yaml");
        }

        /// <summary>Deletes the staged directory tree.</summary>
        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        /// <summary>Writes a placeholder texture file so a cue's texture existence check resolves.</summary>
        /// <remarks>ConfigLoader checks only that the path exists, so the placeholder carries no image data.</remarks>
        /// <param name="fileName">The texture file name exactly as the cue's texture field spells it.</param>
        /// <returns>The absolute path of the written texture file.</returns>
        private string WriteTexture(string fileName)
        {
            string path = Path.Combine(TexturesPath, fileName);
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllBytes(path, Array.Empty<byte>());
            return path;
        }
    }
}
