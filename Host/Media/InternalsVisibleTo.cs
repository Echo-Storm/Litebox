// Exposes LiteBox's internal types (Host.Media helpers etc.) to the offline test runner (LiteBox.Tests).
// InternalsVisibleTo is inert in production — it only widens visibility for a named companion assembly.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("LiteBox.Tests")]
