using System.Diagnostics.CodeAnalysis;
using Avalonia.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;

namespace Avalonia.AnimatedImage;

internal class SingleAnimatedBitmap(Stream stream, bool disposeStream) : IAnimatedBitmap
{
    public bool IsInitialized { get => !IsFailed && field; private set; }

    public bool IsFailed { get; private set; }

    public bool IsCancellable { get; set; }

    public Size Size { get; private set; }
    
    public int FrameCount { get; private set; }

    [field: MaybeNull, AllowNull]
    public IReadOnlyList<Bitmap> Frames
    {
        get => field ?? throw new InvalidOperationException();
        private set;
    }

    public IReadOnlyList<int> Delays { get; private set; } = [];

    public event EventHandler? Initialized;
    
    public event EventHandler<AnimatedBitmapFailedEventArgs>? Failed;
    
    private Stream? _stream = stream ?? throw new ArgumentNullException(nameof(stream));

    public async Task InitAsync(CancellationToken token = default)
    {
        if (IsInitialized || IsFailed)
            return;
        try
        {
            if (_stream is null)
                throw new NullReferenceException(nameof(_stream));
            using var image = await Image.LoadAsync(_stream, token);
            if (disposeStream)
                await _stream.DisposeAsync();
            _stream = null;

            var delays = new int[image.Frames.Count];
            var frames = new Bitmap[image.Frames.Count];
            var index = 0;

            while (image.Frames.Count is not 1)
            {
                if (token.IsCancellationRequested)
                    throw new OperationCanceledException(token);
                using var exportFrame = image.Frames.ExportFrame(0);
                (frames[index], delays[index]) = await GetBitmapAndDelayAsync(exportFrame);
                index++;
            }

            (frames[index], delays[index]) = await GetBitmapAndDelayAsync(image);

            Size = new Size(image.Size.Width, image.Size.Height);
            FrameCount = delays.Length;
            Delays = delays;
            Frames = frames;
            IsInitialized = true;
            Initialized?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e)
        {
            if (_stream is not null && disposeStream)
                await _stream.DisposeAsync();
            _stream = null;
            IsFailed = true;
            Failed?.Invoke(this, new AnimatedBitmapFailedEventArgs(e));
        }

        return;

        async Task<(Bitmap Bitmap, int Delay)> GetBitmapAndDelayAsync(Image frame)
        {
            var webpFrameMetadata = frame.Frames.RootFrame.Metadata.GetWebpMetadata();
            var delay = webpFrameMetadata.FrameDelay is var d && d < 1 ? 10 : (int) d;

            await using var ms = IAnimatedBitmap.RecyclableMemoryStreamManager.GetStream();
            await frame.SaveAsync(ms, _bmpEncoder, token);
            ms.Position = 0;
            var bitmap = new Bitmap(ms);
            return (bitmap, delay);
        }
    }

    private readonly BmpEncoder _bmpEncoder = new()
    {
        BitsPerPixel = BmpBitsPerPixel.Pixel32,
        SupportTransparency = true
    };
}
