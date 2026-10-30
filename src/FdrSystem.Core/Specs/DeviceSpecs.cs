using System;
using System.Collections.Generic;

namespace FdrSystem.Core.Specs;



public static class DeviceSpecs
{
    public sealed record Spec(string Name, string Chip, int RamGb, string Screen, string Camera, int Year);

    private static readonly Dictionary<string, Spec> Map = new(StringComparer.Ordinal)
    {
        
        ["iPhone8,1"] = new("iPhone 6s", "Apple A9", 2, "4,7″", "12 MP", 2015),
        ["iPhone8,2"] = new("iPhone 6s Plus", "Apple A9", 2, "5,5″", "12 MP", 2015),
        ["iPhone8,4"] = new("iPhone SE (1.ª geração)", "Apple A9", 2, "4,0″", "12 MP", 2016),

        
        ["iPhone9,1"] = new("iPhone 7", "Apple A10 Fusion", 2, "4,7″", "12 MP", 2016),
        ["iPhone9,3"] = new("iPhone 7", "Apple A10 Fusion", 2, "4,7″", "12 MP", 2016),
        ["iPhone9,2"] = new("iPhone 7 Plus", "Apple A10 Fusion", 3, "5,5″", "12 MP", 2016),
        ["iPhone9,4"] = new("iPhone 7 Plus", "Apple A10 Fusion", 3, "5,5″", "12 MP", 2016),

        
        ["iPhone10,1"] = new("iPhone 8", "Apple A11 Bionic", 2, "4,7″", "12 MP", 2017),
        ["iPhone10,4"] = new("iPhone 8", "Apple A11 Bionic", 2, "4,7″", "12 MP", 2017),
        ["iPhone10,2"] = new("iPhone 8 Plus", "Apple A11 Bionic", 3, "5,5″", "12 MP", 2017),
        ["iPhone10,5"] = new("iPhone 8 Plus", "Apple A11 Bionic", 3, "5,5″", "12 MP", 2017),
        ["iPhone10,3"] = new("iPhone X", "Apple A11 Bionic", 3, "5,8″", "12 MP", 2017),
        ["iPhone10,6"] = new("iPhone X", "Apple A11 Bionic", 3, "5,8″", "12 MP", 2017),

        
        ["iPhone11,2"] = new("iPhone XS", "Apple A12 Bionic", 4, "5,8″", "12 MP", 2018),
        ["iPhone11,4"] = new("iPhone XS Max", "Apple A12 Bionic", 4, "6,5″", "12 MP", 2018),
        ["iPhone11,6"] = new("iPhone XS Max", "Apple A12 Bionic", 4, "6,5″", "12 MP", 2018),
        ["iPhone11,8"] = new("iPhone XR", "Apple A12 Bionic", 3, "6,1″", "12 MP", 2018),

        
        ["iPhone12,1"] = new("iPhone 11", "Apple A13 Bionic", 4, "6,1″", "12 MP", 2019),
        ["iPhone12,3"] = new("iPhone 11 Pro", "Apple A13 Bionic", 4, "5,8″", "12 MP", 2019),
        ["iPhone12,5"] = new("iPhone 11 Pro Max", "Apple A13 Bionic", 4, "6,5″", "12 MP", 2019),
        ["iPhone12,8"] = new("iPhone SE (2.ª geração)", "Apple A13 Bionic", 3, "4,7″", "12 MP", 2020),

        
        ["iPhone13,1"] = new("iPhone 12 mini", "Apple A14 Bionic", 4, "5,4″", "12 MP", 2020),
        ["iPhone13,2"] = new("iPhone 12", "Apple A14 Bionic", 4, "6,1″", "12 MP", 2020),
        ["iPhone13,3"] = new("iPhone 12 Pro", "Apple A14 Bionic", 6, "6,1″", "12 MP", 2020),
        ["iPhone13,4"] = new("iPhone 12 Pro Max", "Apple A14 Bionic", 6, "6,7″", "12 MP", 2020),

        
        ["iPhone14,2"] = new("iPhone 13 Pro", "Apple A15 Bionic", 6, "6,1″", "12 MP", 2021),
        ["iPhone14,3"] = new("iPhone 13 Pro Max", "Apple A15 Bionic", 6, "6,7″", "12 MP", 2021),
        ["iPhone14,4"] = new("iPhone 13 mini", "Apple A15 Bionic", 4, "5,4″", "12 MP", 2021),
        ["iPhone14,5"] = new("iPhone 13", "Apple A15 Bionic", 4, "6,1″", "12 MP", 2021),
        ["iPhone14,6"] = new("iPhone SE (3.ª geração)", "Apple A15 Bionic", 4, "4,7″", "12 MP", 2022),
        ["iPhone14,7"] = new("iPhone 14", "Apple A15 Bionic", 6, "6,1″", "12 MP", 2022),
        ["iPhone14,8"] = new("iPhone 14 Plus", "Apple A15 Bionic", 6, "6,7″", "12 MP", 2022),

        
        ["iPhone15,2"] = new("iPhone 14 Pro", "Apple A16 Bionic", 6, "6,1″", "48 MP", 2022),
        ["iPhone15,3"] = new("iPhone 14 Pro Max", "Apple A16 Bionic", 6, "6,7″", "48 MP", 2022),

        
        ["iPhone15,4"] = new("iPhone 15", "Apple A16 Bionic", 6, "6,1″", "48 MP", 2023),
        ["iPhone15,5"] = new("iPhone 15 Plus", "Apple A16 Bionic", 6, "6,7″", "48 MP", 2023),
        ["iPhone16,1"] = new("iPhone 15 Pro", "Apple A17 Pro", 8, "6,1″", "48 MP", 2023),
        ["iPhone16,2"] = new("iPhone 15 Pro Max", "Apple A17 Pro", 8, "6,7″", "48 MP", 2023),

        
        ["iPhone17,1"] = new("iPhone 16 Pro", "Apple A18 Pro", 8, "6,3″", "48 MP", 2024),
        ["iPhone17,2"] = new("iPhone 16 Pro Max", "Apple A18 Pro", 8, "6,9″", "48 MP", 2024),
        ["iPhone17,3"] = new("iPhone 16", "Apple A18", 8, "6,1″", "48 MP", 2024),
        ["iPhone17,4"] = new("iPhone 16 Plus", "Apple A18", 8, "6,7″", "48 MP", 2024),
        ["iPhone17,5"] = new("iPhone 16e", "Apple A18", 8, "6,1″", "48 MP", 2025),
    };

    public static Spec? Get(string? productType) =>
        !string.IsNullOrWhiteSpace(productType) && Map.TryGetValue(productType, out var spec) ? spec : null;
}

