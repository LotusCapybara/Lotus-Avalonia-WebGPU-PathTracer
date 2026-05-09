
// this is not a great pattern, it was made for faster prototyping
// I might revisit later
public class Global
{
    public static volatile uint iterationNumber = 0;
    public static volatile float frameEta = 0f;
    public static volatile string frameTimesString = "";
    public static volatile bool isPaused = false;
}