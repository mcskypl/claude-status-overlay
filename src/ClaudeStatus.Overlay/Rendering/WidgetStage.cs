namespace ClaudeStatus.Overlay.Rendering;

/// <summary>
/// Dwa poziomy widgetu, od najmniej do najbardziej natarczywego:
/// <list type="bullet">
///   <item><see cref="Rest"/> - brzeg: cienka linia zawsze widoczna, tylko
///   proporcje limitów i barwny znacznik stanu na środku.</item>
///   <item><see cref="Slim"/> - pasek: podgląd po najechaniu kursorem (albo na
///   stałe, gdy tak ustawione w menu) - pierścień, wartość (czas albo nazwa
///   stanu) i oba mierniki w linii.</item>
/// </list>
/// Trzeciego poziomu - panelu z listą sesji - już nie ma: klik otwiera okno
/// rozmowy, które niesie limity w nagłówku, a stan sesji pokazuje sama pastylka.
/// </summary>
public enum WidgetStage
{
    Rest,
    Slim,
}
