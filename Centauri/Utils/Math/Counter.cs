namespace Centauri.Utils.Math;

public class Counter
{
    public int Value { get; private set; }

    public Counter(int initialValue = 0)
    {
        Value = initialValue;
    }

    public void Increment()
    {
        Value++;
    }

    public void Decrement()
    {
        Value--;
    }

    public void Reset()
    {
        Value = 0;
    }
}