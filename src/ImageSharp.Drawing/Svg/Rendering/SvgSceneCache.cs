// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The retained scenes of one document: a draw of the document that repeats an earlier draw with the same state replays
/// the scene of that draw instead of emitting and preparing its commands again.
/// </summary>
/// <remarks>
/// <para>
/// <b>Policy.</b> The first draw with a key draws directly, as without the cache, and only notes the key. The second draw
/// with the key records the document into a scene (<see cref="DrawingCanvas.RecordScene"/>), keeps it, and renders it
/// through <see cref="DrawingCanvas.RenderScene"/>. Later draws with the key render the kept scene. A recording costs about
/// as much as a direct draw (the same emit and scene build), so a document drawn once pays nothing and a document drawn
/// three times has gained by the third draw.
/// </para>
/// <para>
/// <b>Key.</b> Everything that the emitter, the scene build, and the replay read: the canvas type (the pixel format), the
/// configuration and its allocator by reference and its degree of parallelism, the bounds of the target frame, the
/// destination, and the drawing options of the canvas by value (transform, graphics options, fill rule, text contrast),
/// with floats compared by their bits. The document itself does not change. A draw uses the cache only where a replay is
/// known to give the pixels of a direct draw (<see cref="DrawingCanvas.TryGetSceneTarget"/>: a root canvas on the CPU
/// backend with no open clip and no open layer), and only with an allocator of ImageSharp without an accumulative limit,
/// because the recording takes its memory from another allocator (below).
/// </para>
/// <para>
/// <b>Memory and lifetime.</b> A kept scene is recorded with <see cref="SvgSceneAllocator"/>, so it holds managed memory
/// only, and it is never disposed: a canvas that renders it later, or an outer scene recorded from such a canvas, can still
/// use it after the cache drops it, and the garbage collector frees it when nothing uses it. The document is not
/// disposable, so this is the only release point that every user of the scene keeps. At most <see cref="MaxScenes"/>
/// scenes are kept, together at most <see cref="MaxBytes"/> bytes; the least recently used scene goes first. The bytes of
/// a scene are counted as the managed bytes that its recording allocated on every thread, less the working buffers that
/// the recording released through its allocator. The count also holds the managed working objects of the emit and of the
/// scene build, so it is at least what the scene keeps: for the benchmark documents at 100, 1000, and 4000 pixels it was
/// 1.02 to 7.4 times the growth of the managed heap that holding the scene causes. It does not hold the brush renderers
/// that the first replay adds to the scene, one small object per item. A scene above the byte bound is not kept; its key
/// is noted so that it is not recorded again.
/// </para>
/// <para>
/// <b>Threads.</b> A lock guards the slots only while a draw looks up or stores a key; no lock is held while a scene is
/// recorded or rendered. A draw whose key is being recorded by another draw draws directly. A kept scene can render on
/// several canvases at the same time.
/// </para>
/// </remarks>
internal sealed class SvgSceneCache
{
    /// <summary>
    /// The largest number of scenes that a document keeps: a document drawn at a few sizes in turn, such as a thumbnail and
    /// a full view, keeps one scene for each. The pattern tiles of a document keep as many sizes.
    /// </summary>
    internal const int MaxScenes = 4;

    /// <summary>
    /// The largest number of bytes that the kept scenes of a document hold together. The largest measured scene of the
    /// benchmark documents that the cache is for (TextHeavy, TextDecorations, Tiger, Icons) holds 9 MB at 4000 pixels, the
    /// filter documents at 4000 pixels hold 36 to 47 MB, and a scene of the 30,000-path map holds 42 to 95 MB, which is
    /// not kept.
    /// </summary>
    internal const long MaxBytes = 64L * 1024 * 1024;

    /// <summary>
    /// The number of keys that a document notes as drawn once, or as too large to keep.
    /// </summary>
    private const int MaxMarks = 8;

    private readonly Slot?[] kept;
    private readonly Slot?[] marks = new Slot?[MaxMarks];
    private readonly long maxBytes;
    private long clock;
    private long keptBytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgSceneCache"/> class with the bounds <see cref="MaxScenes"/> and
    /// <see cref="MaxBytes"/>.
    /// </summary>
    public SvgSceneCache()
        : this(MaxScenes, MaxBytes)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgSceneCache"/> class with other bounds, for tests of the bounds.
    /// </summary>
    /// <param name="maxScenes">The largest number of kept scenes. Greater than zero.</param>
    /// <param name="maxBytes">The largest number of counted bytes of the kept scenes together.</param>
    internal SvgSceneCache(int maxScenes, long maxBytes)
    {
        this.kept = new Slot?[maxScenes];
        this.maxBytes = maxBytes;
    }

    /// <summary>
    /// The states of a slot.
    /// </summary>
    private enum SlotState
    {
        /// <summary>
        /// The key was drawn once (a mark).
        /// </summary>
        Seen,

        /// <summary>
        /// The scene of the key was too large to keep (a mark).
        /// </summary>
        Rejected,

        /// <summary>
        /// A draw records the scene of the key (a kept slot without a scene yet).
        /// </summary>
        Recording,

        /// <summary>
        /// The scene of the key is kept.
        /// </summary>
        Kept,
    }

    /// <summary>
    /// Gets the number of kept scenes.
    /// </summary>
    internal int KeptSceneCount
    {
        get
        {
            lock (this.kept)
            {
                int count = 0;
                foreach (Slot? slot in this.kept)
                {
                    if (slot?.State == SlotState.Kept)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
    }

    /// <summary>
    /// Gets the counted bytes of the kept scenes.
    /// </summary>
    internal long KeptBytes
    {
        get
        {
            lock (this.kept)
            {
                return this.keptBytes;
            }
        }
    }

    /// <summary>
    /// Draws a document through its kept scene, or records the scene on the second draw with the same key.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="document">The document that owns this cache.</param>
    /// <param name="viewport">The viewport transform of the draw.</param>
    /// <param name="destination">The destination rectangle of the draw. Its width and height are greater than zero.</param>
    /// <returns>
    /// <see langword="true"/> if the draw is done; <see langword="false"/> if the caller must draw the document directly.
    /// </returns>
    public bool TryDraw(DrawingCanvas canvas, SvgDocument document, Matrix3x2 viewport, RectangleF destination)
    {
        if (!canvas.TryGetSceneTarget(out Rectangle targetBounds))
        {
            return false;
        }

        // The recording uses its own allocator. An allocator with an accumulative limit accounts every byte of the draws
        // of the caller, and an allocator of another library can have rules of its own, so neither is bypassed.
        Configuration configuration = canvas.GetConfiguration();
        MemoryAllocator allocator = configuration.MemoryAllocator;
        if (allocator.AccumulativeAllocationLimitBytes != long.MaxValue
            || allocator.GetType().Assembly != typeof(MemoryAllocator).Assembly)
        {
            return false;
        }

        DrawingOptions options = canvas.GetActiveOptions();
        Key key = new(canvas.GetType(), configuration, targetBounds, destination, options);

        Slot? recording;
        DrawingBackendScene? keptScene;
        lock (this.kept)
        {
            long now = ++this.clock;
            Slot? slot = Find(this.kept, in key);
            if (slot is not null)
            {
                if (slot.State != SlotState.Kept)
                {
                    // Another draw records this key now. Waiting for it could take as long as drawing.
                    return false;
                }

                slot.LastUse = now;
                keptScene = slot.Scene;
                recording = null;
            }
            else
            {
                recording = this.TryBeginRecording(in key, now);
                if (recording is null)
                {
                    return false;
                }

                keptScene = null;
            }
        }

        if (recording is null)
        {
            // A null scene is a document that draws nothing at this placement.
            if (keptScene is not null)
            {
                canvas.RenderScene(keptScene);
            }

            return true;
        }

        return this.RecordAndDraw(canvas, document, viewport, destination, configuration, options, recording);
    }

    /// <summary>
    /// Notes a key that has no kept slot. On the first draw with the key it stores a mark; on the second it takes a kept
    /// slot for the recording.
    /// </summary>
    /// <remarks>Call it under the lock.</remarks>
    /// <param name="key">The key.</param>
    /// <param name="now">The clock of this draw.</param>
    /// <returns>The recording slot, or <see langword="null"/> when this draw draws directly.</returns>
    private Slot? TryBeginRecording(in Key key, long now)
    {
        Slot? mark = Find(this.marks, in key);
        if (mark is null)
        {
            Store(this.marks, new Slot(key, SlotState.Seen, now));
            return null;
        }

        mark.LastUse = now;
        if (mark.State == SlotState.Rejected)
        {
            return null;
        }

        // The second draw with the key: take a kept slot for the recording, dropping the least recently used scene when
        // every slot is taken. When every slot is being recorded, this draw draws directly.
        Slot recording = new(key, SlotState.Recording, now);
        if (!this.TryReserve(recording))
        {
            return null;
        }

        Remove(this.marks, mark);
        return recording;
    }

    /// <summary>
    /// Records the scene of a draw, keeps it when it fits the bound, and renders it on the canvas.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="document">The document.</param>
    /// <param name="viewport">The viewport transform.</param>
    /// <param name="destination">The destination rectangle.</param>
    /// <param name="configuration">The configuration of the canvas.</param>
    /// <param name="options">The drawing options of the canvas.</param>
    /// <param name="recording">The kept slot that this draw took for the recording.</param>
    /// <returns><see langword="true"/>: the draw is done.</returns>
    private bool RecordAndDraw(
        DrawingCanvas canvas,
        SvgDocument document,
        Matrix3x2 viewport,
        RectangleF destination,
        Configuration configuration,
        DrawingOptions options,
        Slot recording)
    {
        DrawingBackendScene? scene;
        long bytes;
        try
        {
            scene = Record(canvas, document, viewport, destination, configuration, options, out bytes);
        }
        catch
        {
            lock (this.kept)
            {
                Remove(this.kept, recording);
            }

            throw;
        }

        lock (this.kept)
        {
            if (bytes > this.maxBytes)
            {
                // The scene renders this time and is not kept. The mark keeps the key from being recorded again.
                Remove(this.kept, recording);
                Store(this.marks, new Slot(recording.Key, SlotState.Rejected, recording.LastUse));
            }
            else
            {
                this.MakeRoom(bytes, recording);
                recording.Scene = scene;
                recording.Bytes = bytes;
                recording.State = SlotState.Kept;
                this.keptBytes += bytes;
            }
        }

        if (scene is not null)
        {
            canvas.RenderScene(scene);
        }

        return true;
    }

    /// <summary>
    /// Records a document into a scene of managed memory and counts the bytes that the scene keeps.
    /// </summary>
    /// <param name="canvas">The canvas that the scene is for.</param>
    /// <param name="document">The document.</param>
    /// <param name="viewport">The viewport transform.</param>
    /// <param name="destination">The destination rectangle.</param>
    /// <param name="configuration">The configuration of the canvas.</param>
    /// <param name="options">The drawing options of the canvas.</param>
    /// <param name="bytes">Receives the counted bytes of the scene.</param>
    /// <returns>The scene, or <see langword="null"/> when the document records nothing.</returns>
    private static DrawingBackendScene? Record(
        DrawingCanvas canvas,
        SvgDocument document,
        Matrix3x2 viewport,
        RectangleF destination,
        Configuration configuration,
        DrawingOptions options,
        out long bytes)
    {
        // A copy of the configuration with the managed allocator. The properties carry the drawing backend that the
        // nested canvases of filters use; Clone does not copy them.
        SvgSceneAllocator allocator = new(configuration.MemoryAllocator);
        Configuration recordingConfiguration = configuration.Clone();
        foreach (KeyValuePair<object, object> property in configuration.Properties)
        {
            recordingConfiguration.Properties[property.Key] = property.Value;
        }

        recordingConfiguration.MemoryAllocator = allocator;

        // The commands keep references to the graphics options of the canvas state, which the caller can change after
        // this draw, so the recording draws under a copy.
        DrawingOptions recordingOptions = new(
            options.GraphicsOptions.DeepClone(),
            options.IntersectionRule,
            options.Transform,
            options.TextContrast);

        long start = GC.GetTotalAllocatedBytes(precise: true);
        DrawingBackendScene? scene = canvas.RecordScene(
            recordingConfiguration,
            recordingOptions,
            recorder => SvgEmitter.Draw(recorder, document, viewport, destination));

        // Everything that the recording allocated, on every thread, less the buffers that it released (see the remarks of
        // the class). Allocations of other threads in the meantime make the count larger, never smaller.
        bytes = GC.GetTotalAllocatedBytes(precise: true) - start - allocator.ReleasedBytes;
        return scene;
    }

    /// <summary>
    /// Returns the slot of a key.
    /// </summary>
    /// <param name="slots">The slots.</param>
    /// <param name="key">The key.</param>
    /// <returns>The slot, or <see langword="null"/>.</returns>
    private static Slot? Find(Slot?[] slots, in Key key)
    {
        foreach (Slot? slot in slots)
        {
            if (slot is not null && slot.Key.Equals(in key))
            {
                return slot;
            }
        }

        return null;
    }

    /// <summary>
    /// Stores a mark in a free slot, or in place of the least recently used mark.
    /// </summary>
    /// <param name="slots">The mark slots.</param>
    /// <param name="slot">The mark.</param>
    private static void Store(Slot?[] slots, Slot slot)
    {
        int index = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] is null)
            {
                index = i;
                break;
            }

            if (slots[i]!.LastUse < slots[index]!.LastUse)
            {
                index = i;
            }
        }

        slots[index] = slot;
    }

    /// <summary>
    /// Removes a slot.
    /// </summary>
    /// <param name="slots">The slots.</param>
    /// <param name="slot">The slot to remove.</param>
    private static void Remove(Slot?[] slots, Slot slot)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (ReferenceEquals(slots[i], slot))
            {
                slots[i] = null;
                return;
            }
        }
    }

    /// <summary>
    /// Puts a recording slot into a free kept slot, or in place of the least recently used kept scene.
    /// </summary>
    /// <param name="slot">The recording slot.</param>
    /// <returns><see langword="true"/> if the slot is stored; <see langword="false"/> if every slot is being recorded.</returns>
    private bool TryReserve(Slot slot)
    {
        int index = -1;
        for (int i = 0; i < this.kept.Length; i++)
        {
            Slot? candidate = this.kept[i];
            if (candidate is null)
            {
                index = i;
                break;
            }

            if (candidate.State == SlotState.Kept && (index < 0 || candidate.LastUse < this.kept[index]!.LastUse))
            {
                index = i;
            }
        }

        if (index < 0)
        {
            return false;
        }

        this.Drop(index);
        this.kept[index] = slot;
        return true;
    }

    /// <summary>
    /// Drops the least recently used kept scenes, other than <paramref name="keep"/>, until <paramref name="bytes"/> more
    /// bytes fit in the byte bound.
    /// </summary>
    /// <param name="bytes">The bytes of the scene to add. At most the byte bound.</param>
    /// <param name="keep">The slot of the scene to add.</param>
    private void MakeRoom(long bytes, Slot keep)
    {
        while (this.keptBytes + bytes > this.maxBytes)
        {
            int index = -1;
            for (int i = 0; i < this.kept.Length; i++)
            {
                Slot? candidate = this.kept[i];
                if (candidate is { State: SlotState.Kept } && !ReferenceEquals(candidate, keep)
                    && (index < 0 || candidate.LastUse < this.kept[index]!.LastUse))
                {
                    index = i;
                }
            }

            // The kept bytes are the bytes of the kept scenes, so a scene remains while they exceed the bound.
            this.Drop(index);
        }
    }

    /// <summary>
    /// Empties a kept slot. The scene is not disposed (see the remarks of the class).
    /// </summary>
    /// <param name="index">The slot index.</param>
    private void Drop(int index)
    {
        Slot? slot = this.kept[index];
        if (slot is null)
        {
            return;
        }

        if (slot.State == SlotState.Kept)
        {
            this.keptBytes -= slot.Bytes;
        }

        this.kept[index] = null;
    }

    /// <summary>
    /// The state of a draw that a scene depends on.
    /// </summary>
    private readonly struct Key
    {
        private readonly Type canvasType;
        private readonly Configuration configuration;
        private readonly MemoryAllocator allocator;
        private readonly int maxDegreeOfParallelism;
        private readonly Rectangle targetBounds;
        private readonly RectangleF destination;
        private readonly Matrix4x4 transform;
        private readonly float blendPercentage;
        private readonly float textContrast;
        private readonly int modes;

        /// <summary>
        /// Initializes a new instance of the <see cref="Key"/> struct.
        /// </summary>
        /// <param name="canvasType">The type of the canvas, which names its pixel format.</param>
        /// <param name="configuration">The configuration of the canvas.</param>
        /// <param name="targetBounds">The bounds of the target frame.</param>
        /// <param name="destination">The destination rectangle.</param>
        /// <param name="options">The drawing options of the canvas state.</param>
        public Key(Type canvasType, Configuration configuration, Rectangle targetBounds, RectangleF destination, DrawingOptions options)
        {
            GraphicsOptions graphics = options.GraphicsOptions;
            this.canvasType = canvasType;
            this.configuration = configuration;
            this.allocator = configuration.MemoryAllocator;
            this.maxDegreeOfParallelism = configuration.MaxDegreeOfParallelism;
            this.targetBounds = targetBounds;
            this.destination = destination;
            this.transform = options.Transform;
            this.blendPercentage = graphics.BlendPercentage;
            this.textContrast = options.TextContrast;

            // The enumerations have fewer than 256 values each.
            this.modes = (graphics.Antialias ? 1 : 0)
                | ((int)options.IntersectionRule << 1)
                | ((int)graphics.ColorBlendingMode << 8)
                | ((int)graphics.AlphaCompositionMode << 16);
        }

        /// <summary>
        /// Returns whether two keys are equal: the same objects, and floats with the same bits.
        /// </summary>
        /// <param name="other">The other key.</param>
        /// <returns><see langword="true"/> if the keys are equal; otherwise, <see langword="false"/>.</returns>
        public bool Equals(in Key other)
            => this.canvasType == other.canvasType
            && ReferenceEquals(this.configuration, other.configuration)
            && ReferenceEquals(this.allocator, other.allocator)
            && this.maxDegreeOfParallelism == other.maxDegreeOfParallelism
            && this.targetBounds == other.targetBounds
            && this.modes == other.modes
            && SameBits(this.blendPercentage, other.blendPercentage)
            && SameBits(this.textContrast, other.textContrast)
            && SameBits(in this.destination, in other.destination)
            && SameBits(in this.transform, in other.transform);

        /// <summary>
        /// Returns whether two floats have the same bits. Equality of values would treat <c>-0</c> and <c>+0</c> as equal,
        /// although they can round differently further on.
        /// </summary>
        /// <param name="a">The first value.</param>
        /// <param name="b">The second value.</param>
        /// <returns><see langword="true"/> if the bits are equal.</returns>
        private static bool SameBits(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

        /// <summary>
        /// Returns whether two values of an unmanaged type have the same bytes.
        /// </summary>
        /// <typeparam name="T">The type.</typeparam>
        /// <param name="a">The first value.</param>
        /// <param name="b">The second value.</param>
        /// <returns><see langword="true"/> if the bytes are equal.</returns>
        private static bool SameBits<T>(in T a, in T b)
            where T : unmanaged
            => MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in a)).SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in b)));
    }

    /// <summary>
    /// One key with its state.
    /// </summary>
    private sealed class Slot
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Slot"/> class.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="state">The state.</param>
        /// <param name="lastUse">The clock of the last use.</param>
        public Slot(Key key, SlotState state, long lastUse)
        {
            this.Key = key;
            this.State = state;
            this.LastUse = lastUse;
        }

        /// <summary>
        /// Gets the key.
        /// </summary>
        public Key Key { get; }

        /// <summary>
        /// Gets or sets the state.
        /// </summary>
        public SlotState State { get; set; }

        /// <summary>
        /// Gets or sets the clock of the last use, for the least recently used order.
        /// </summary>
        public long LastUse { get; set; }

        /// <summary>
        /// Gets or sets the kept scene, or <see langword="null"/> when the document records nothing for the key.
        /// </summary>
        public DrawingBackendScene? Scene { get; set; }

        /// <summary>
        /// Gets or sets the counted bytes of the kept scene.
        /// </summary>
        public long Bytes { get; set; }
    }
}
