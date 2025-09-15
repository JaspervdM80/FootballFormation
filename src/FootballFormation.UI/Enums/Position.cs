namespace FootballFormation.UI.Enums;

[Flags]
public enum Position
{
    None = 0,
    
    // Goalkeeper
    GK = 1,
    
    // Defenders
    DC = 2,   // Center Back (Defender Center)
    DL = 4,   // Left Back (Defender Left) 
    DR = 8,   // Right Back (Defender Right)
    
    // Midfielders
    CDM = 16, // Defensive Midfielder
    CAM = 32, // Attacking Midfielder
    
    // Attackers
    LW = 64,  // Left Wing
    ST = 128, // Striker
    RW = 256, // Right Wing
    
    // Position groups for easier checking
    Defenders = DC | DL | DR,
    Midfielders = CDM | CAM,
    Attackers = LW | ST | RW
}
