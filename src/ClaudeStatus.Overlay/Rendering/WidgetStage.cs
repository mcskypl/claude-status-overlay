namespace ClaudeStatus.Overlay.Rendering;

/// <summary>
/// Trzy poziomy widgetu, od najmniej do najbardziej natarczywego:
/// <list type="bullet">
///   <item><see cref="Rest"/> - brzeg: cienka linia zawsze widoczna, tylko
///   proporcje limitów i barwny znacznik stanu na środku.</item>
///   <item><see cref="Slim"/> - pasek: podgląd po najechaniu kursorem -
///   pierścień, wartość (czas albo nazwa stanu) i oba mierniki w linii.</item>
///   <item><see cref="Panel"/> - pełna lista sesji po kliknięciu; zostaje
///   otwarty, dopóki nie kliknie się ponownie (najazd/zjazd kursorem go nie rusza).</item>
/// </list>
/// </summary>
public enum WidgetStage
{
    Rest,
    Slim,
    Panel,
}
