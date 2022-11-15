public class NUnitLoggerProvider : ILoggerProvider {
	public ILogger CreateLogger(string categoryName) {
		return new NUnitLogger();
	}

	public void Dispose() {}

	public class NUnitLogger : ILogger, IDisposable{
		public void Dispose() {}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) {
			var message = formatter(state, exception);
			Console.WriteLine(message);
		}

		public bool IsEnabled(LogLevel logLevel) => true;

		public IDisposable BeginScope<TState>(TState state) => this;
	}
}