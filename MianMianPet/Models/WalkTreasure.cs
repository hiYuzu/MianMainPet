namespace MianMianPet.Models;

internal sealed record WalkTreasure(string Name, string Encounter, string Shape, string Color);

internal static class WalkTreasures
{
    internal static readonly IReadOnlyList<WalkTreasure> All =
    [
        new("樱花发夹", "路边的樱花落在耳朵上，风说这朵送给你。",
            "M20,16 C5,0 0,18 13,22 C-2,33 15,42 20,28 C25,42 42,33 27,22 C40,18 35,0 20,16Z M17,22 A3,3 0 1 0 23,22 A3,3 0 1 0 17,22", "#F5B7CD"),
        new("星星胸针", "草尖上躺着一颗小星星，晒得暖暖的。",
            "M20,2 L25,14 38,16 28,25 31,38 20,31 9,38 12,25 2,16 15,14Z", "#F8DB98"),
        new("月牙吊坠", "池塘把月亮借给我，还附送了一圈小涟漪。",
            "M25,3 C4,0 -2,30 19,37 C27,40 35,35 38,28 C18,34 10,12 25,3Z", "#DDD0F2"),
        new("薄荷叶饰", "一只蝴蝶领我找到薄荷，连风都变得清甜啦。",
            "M6,34 C-1,12 17,2 35,4 C38,25 24,40 6,34Z M6,34 L28,11 M16,24 L13,14 M22,18 L31,19", "#BFDFCC"),
        new("云朵别针", "山坡上有朵迷路的小云，我牵着它慢慢走回来了。",
            "M9,31 C-1,31 -1,17 8,16 C5,2 23,0 26,12 C37,7 44,26 33,31Z", "#FFF5FA"),
        new("蜜桃蝴蝶结", "小鸟在树枝上系了个蝴蝶结，说今天也要漂漂亮亮。",
            "M18,18 C-4,-1 -3,40 18,25 L14,38 23,33 30,37 25,25 C46,39 43,-1 23,18Z M17,18 L24,18 24,26 17,26Z", "#F3B6A7")
    ];
}
