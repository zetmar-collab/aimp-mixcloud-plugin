using System;
using System.Threading;
using Mixcloud.Core.Process;
using Xunit;

public class ProcessRunnerTests
{
    private static readonly string Cmd =
        Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\cmd.exe");

    [Fact]
    public void PrzechwytujeStandardoweWyjscie()
    {
        var res = new ProcessRunner().Run(Cmd, "/c echo hello", TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.False(res.TimedOut);
        Assert.Equal(0, res.ExitCode);
        Assert.Contains("hello", res.StdOut);
    }

    [Fact]
    public void PrzechwytujeStandardowyBlad()
    {
        var res = new ProcessRunner().Run(Cmd, "/c echo problem 1>&2", TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.Contains("problem", res.StdErr);
    }

    [Fact]
    public void ZwracaNiezerowyKodWyjscia()
    {
        var res = new ProcessRunner().Run(Cmd, "/c exit 3", TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.False(res.TimedOut);
        Assert.Equal(3, res.ExitCode);
    }

    [Fact]
    public void ZabijaProcesPoPrzekroczeniuTimeoutu()
    {
        var start = DateTime.UtcNow;
        var res = new ProcessRunner().Run(Cmd, "/c ping -n 30 127.0.0.1 > nul",
            TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.True(res.TimedOut);
        // Musi wrocic po timeoucie, a nie po zakonczeniu 30-sekundowego procesu.
        Assert.True(DateTime.UtcNow - start < TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void AnulowanieKonczyProcesPrzedTimeoutem()
    {
        var start = DateTime.UtcNow;
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
        {
            var res = new ProcessRunner().Run(Cmd, "/c ping -n 30 127.0.0.1 > nul",
                TimeSpan.FromMinutes(5), cts.Token);

            Assert.True(res.TimedOut);
            // Musi wrocic po anulowaniu, a nie po zakonczeniu 30-sekundowego procesu.
            Assert.True(DateTime.UtcNow - start < TimeSpan.FromSeconds(15));
        }
    }

    [Fact]
    public void DuzeWyjscieNieZakleszczaOdczytu()
    {
        // Synchroniczny ReadToEnd na obu strumieniach zakleszcza sie, gdy proces
        // zapelni bufor. Ten test pilnuje, ze odczyt jest asynchroniczny.
        var res = new ProcessRunner().Run(Cmd,
            "/c for /L %i in (1,1,2000) do @echo wiersz-wypelniajacy-bufor-%i",
            TimeSpan.FromSeconds(60), CancellationToken.None);

        Assert.False(res.TimedOut);
        Assert.Equal(0, res.ExitCode);
        Assert.Contains("wiersz-wypelniajacy-bufor-2000", res.StdOut);
    }
}
