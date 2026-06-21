namespace BookTrak.Data.Entities;

/// <summary>Work-level reading state. Every transition is skippable and reversible.</summary>
public enum BookStatus
{
    None = 0,
    WantToRead = 1,
    Reading = 2,
    Read = 3,
}
