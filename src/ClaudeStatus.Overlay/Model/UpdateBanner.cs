namespace ClaudeStatus.Overlay.Model;

/// <summary>
/// Pasek o aktualizacji na dole panelu; null = panel nic o niej nie mówi.
/// Renderer nie wie nic o pobieraniu - dostaje gotowy napis i to, czy klik
/// ma sens (podczas pobierania nie ma).
/// </summary>
/// <param name="Text">Gotowy napis, np. "nowa wersja 2.1.0 - zaktualizuj".</param>
/// <param name="Actionable">Czy kliknięcie wiersza coś zrobi.</param>
/// <param name="Progress">Postęp pobierania 0..1 (ujemny albo null = brak paska postępu).</param>
public sealed record UpdateBanner(string Text, bool Actionable, double? Progress = null);
