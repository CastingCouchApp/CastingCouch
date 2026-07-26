# Creator Control Suite 8.0.0-alpha66

## Early-startup crash handling

- Global WPF, AppDomain and task exception handlers are now registered immediately after `base.OnStartup`.
- The complete asynchronous startup sequence is protected by a central `try/catch`.
- Failures before dependency injection and logging are fully initialized are written to the bootstrap log.
- Startup failures now attempt to create a crash report and display a clear error dialog.
- The application exits with a failure code after an unrecoverable startup error.
- Product version updated to `8.0.0-alpha66`.
