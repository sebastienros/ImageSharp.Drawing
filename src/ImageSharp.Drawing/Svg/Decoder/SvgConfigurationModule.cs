// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Registers the SVG format, its detection, and <see cref="SvgDecoder"/> in a <see cref="Configuration"/>.
/// </summary>
/// <remarks>
/// <para>
/// SVG is not registered by default: referencing this package does not change <see cref="Configuration.Default"/>. A
/// caller opts in for one configuration with <c>configuration.Configure(new SvgConfigurationModule())</c>, or creates a
/// configuration with <see cref="Configuration.Configuration(IImageFormatConfigurationModule[])"/> and lists the module. After that,
/// <c>Image.Load</c>, <c>Image.Identify</c>, and <c>Image.DetectFormat</c> with that configuration read SVG and
/// <c>.svgz</c> data, with the default <see cref="SvgDecoderOptions"/>. There is no SVG encoder.
/// </para>
/// <para>
/// <see cref="Configuration.Clone"/> shares the format registrations of the original, so registering the module on a
/// clone of <see cref="Configuration.Default"/> also changes <see cref="Configuration.Default"/>.
/// </para>
/// <para>
/// The module adds two detectors: <see cref="SvgImageFormatDetector"/>, which makes the configuration read 512 bytes for
/// the detection of any format, and one for data shorter than that, which ImageSharp would otherwise not pass to it.
/// </para>
/// </remarks>
public sealed class SvgConfigurationModule : IImageFormatConfigurationModule
{
    /// <summary>
    /// Registers the SVG format, its detectors, and <see cref="SvgDecoder.Instance"/> in a configuration.
    /// </summary>
    /// <param name="configuration">The configuration to change.</param>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    public void Configure(Configuration configuration)
    {
        Guard.NotNull(configuration, nameof(configuration));

        ImageFormatManager formats = configuration.ImageFormatsManager;
        formats.SetDecoder(SvgFormat.Instance, SvgDecoder.Instance);
        formats.AddImageFormatDetector(new SvgImageFormatDetector());
        formats.AddImageFormatDetector(new SvgShortDataFormatDetector());
    }
}
