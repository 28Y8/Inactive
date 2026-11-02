using Inactive.Core.Logging;
using Inactive.Core.Security;

namespace Inactive.Core.Locking;

/// <summary>
/// Checks PINs on a dedicated thread, because PBKDF2 would otherwise stall the hook and Windows
/// would drop it. <see cref="Submit"/> only copies the digits, so it is safe inside the hook.
/// </summary>
internal sealed class PinVerifier
{
    private readonly Lock _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly PinCredential _credential;
    private readonly Action<long, bool> _onResult;

    private readonly char[] _pending = GC.AllocateArray<char>(PinHasher.MaxLength, pinned: true);
    private int _pendingLength;
    private long _pendingToken;
    private volatile bool _stopping;

    public PinVerifier(PinCredential credential, Action<long, bool> onResult)
    {
        _credential = credential;
        _onResult = onResult;
    }

    public void Start() =>
        new Thread(Run) { IsBackground = true, Name = "Inactive PIN verifier" }.Start();

    public void Submit(ReadOnlySpan<char> pin, long token)
    {
        lock (_gate)
        {
            // Nothing would ever clear the digits once the thread has gone.
            if (_stopping)
            {
                return;
            }

            pin.CopyTo(_pending);
            _pendingLength = pin.Length;
            _pendingToken = token;
        }

        _wake.Set();
    }

    /// <summary>Stops the thread without waiting. A check in flight ends as a stale result.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _stopping = true;
            Array.Clear(_pending);
            _pendingLength = 0;
        }

        _wake.Set();
    }

    private void Run()
    {
        Span<char> pin = stackalloc char[PinHasher.MaxLength];

        while (true)
        {
            _wake.WaitOne();
            if (_stopping)
            {
                return;
            }

            int length;
            long token;
            lock (_gate)
            {
                length = _pendingLength;
                token = _pendingToken;
                _pending.AsSpan(0, length).CopyTo(pin);
                Array.Clear(_pending);
                _pendingLength = 0;
            }

            if (length > 0)
            {
                _onResult(token, Check(pin[..length]));
                pin.Clear();
            }
        }
    }

    private bool Check(ReadOnlySpan<char> pin)
    {
        try
        {
            return PinHasher.Verify(pin, _credential);
        }
        catch (Exception e)
        {
            AppLog.Error("A PIN check failed.", e);
            return false;
        }
    }
}
