using Marten;

namespace ProductCatalog.Products;

/// <summary>
/// Seed data for Product Catalog BC.
/// Provides sample products for development and testing.
/// </summary>
public static class SeedData
{
    public static async Task SeedProductsAsync(IDocumentStore store, CancellationToken ct = default)
    {
        await using var session = store.LightweightSession();

        // Check if products already exist
        var existingCount = await session.Query<Product>().CountAsync(ct);
        if (existingCount > 0)
        {
            return; // Already seeded
        }

        var products = new List<Product>
        {
            // Dog Products
            Product.Create(
                "DOG-BOWL-001",
                "Premium Stainless Steel Dog Bowl",
                "Dishwasher-safe stainless steel bowl with non-slip rubber base.",
                "Dogs",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Dog+Bowl", "Stainless steel dog bowl", 0)
                }.ToList().AsReadOnly(),
                "Premium stainless steel construction ensures durability and easy cleaning. Non-slip rubber base keeps bowl in place during feeding.",
                "Bowls & Feeders",
                "PetSupreme",
                new[] { "dishwasher-safe", "non-slip", "stainless-steel" }.ToList().AsReadOnly(),
                ProductDimensions.Create(8, 8, 3, 1.2m)),

            Product.Create(
                "DOG-TOY-ROPE",
                "Durable Rope Toy - Large",
                "Heavy-duty rope toy perfect for tug-of-war and fetch.",
                "Dogs",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Rope+Toy", "Dog rope toy", 0)
                }.ToList().AsReadOnly(),
                "Made from durable cotton rope fibers. Great for dental health and interactive play.",
                "Toys",
                "PlayPaws",
                new[] { "durable", "dental-health", "interactive" }.ToList().AsReadOnly(),
                ProductDimensions.Create(14, 2, 2, 0.6m)),

            Product.Create(
                "DOG-COLLAR-LED",
                "LED Safety Collar - Adjustable",
                "Light-up collar for nighttime walks and visibility.",
                "Dogs",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=LED+Collar", "LED dog collar", 0)
                }.ToList().AsReadOnly(),
                "USB rechargeable LED collar with three light modes. Adjustable to fit most dog sizes.",
                "Collars & Leashes",
                "SafePet",
                new[] { "led", "rechargeable", "safety", "adjustable" }.ToList().AsReadOnly(),
                ProductDimensions.Create(24, 1, 0.5m, 0.3m)),

            Product.Create(
                "DOG-BED-ORTHO",
                "Orthopedic Memory Foam Dog Bed",
                "Supportive memory foam bed for senior dogs and large breeds.",
                "Dogs",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Dog+Bed", "Orthopedic dog bed", 0)
                }.ToList().AsReadOnly(),
                "Premium memory foam provides joint support. Removable, machine-washable cover.",
                "Beds & Furniture",
                "ComfortPaws",
                new[] { "orthopedic", "memory-foam", "washable" }.ToList().AsReadOnly(),
                ProductDimensions.Create(36, 27, 4, 8.5m)),

            Product.Create(
                "DOG-TREATS-CHK",
                "Chicken Jerky Treats (12oz)",
                "All-natural chicken jerky treats made in USA.",
                "Dogs",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Chicken+Treats", "Chicken jerky treats", 0)
                }.ToList().AsReadOnly(),
                "Single-ingredient chicken jerky. No artificial preservatives or additives. Made in USA.",
                "Treats & Snacks",
                "NaturalBite",
                new[] { "natural", "usa-made", "single-ingredient" }.ToList().AsReadOnly(),
                ProductDimensions.Create(8, 6, 2, 0.75m)),

            // Cat Products
            Product.Create(
                "CAT-TREE-5FT",
                "Multi-Level Cat Tree - 5ft",
                "Five-tier cat tree with scratching posts and cozy condos.",
                "Cats",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Cat+Tree", "Multi-level cat tree", 0)
                }.ToList().AsReadOnly(),
                "Features multiple perches, two enclosed condos, sisal scratching posts, and hanging toys.",
                "Furniture & Scratchers",
                "FelineFun",
                new[] { "multi-level", "scratching-post", "condo" }.ToList().AsReadOnly(),
                ProductDimensions.Create(24, 24, 60, 45m)),

            Product.Create(
                "CAT-LITTER-CLM",
                "Clumping Clay Litter (25lb)",
                "Odor-control clumping litter for easy cleanup.",
                "Cats",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Cat+Litter", "Clumping cat litter", 0)
                }.ToList().AsReadOnly(),
                "Fast-clumping formula locks in odors. 99% dust-free. Unscented.",
                "Litter & Accessories",
                "FreshPaws",
                new[] { "clumping", "odor-control", "dust-free" }.ToList().AsReadOnly(),
                ProductDimensions.Create(18, 12, 5, 25m)),

            Product.Create(
                "CAT-TOY-LASER",
                "Interactive Laser Pointer Toy",
                "Automatic laser toy with multiple patterns for cats.",
                "Cats",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Laser+Toy", "Laser pointer toy", 0)
                }.ToList().AsReadOnly(),
                "Battery-powered automatic laser with five random patterns. 15-minute auto-shutoff timer.",
                "Toys",
                "PlayKitty",
                new[] { "automatic", "laser", "interactive" }.ToList().AsReadOnly(),
                ProductDimensions.Create(4, 4, 2, 0.4m)),

            Product.Create(
                "CAT-FOUNTAIN-2L",
                "Pet Water Fountain - 2 Liter",
                "Circulating water fountain encourages hydration.",
                "Cats",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Water+Fountain", "Cat water fountain", 0)
                }.ToList().AsReadOnly(),
                "Ultra-quiet pump circulates water through triple-filtration system. 2-liter capacity.",
                "Bowls & Feeders",
                "HydratePet",
                new[] { "fountain", "filter", "quiet" }.ToList().AsReadOnly(),
                ProductDimensions.Create(8, 8, 6, 2.1m)),

            Product.Create(
                "CAT-CARRIER-SM",
                "Soft-Sided Pet Carrier - Small",
                "Airline-approved carrier for cats and small dogs.",
                "Cats",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Pet+Carrier", "Soft-sided carrier", 0)
                }.ToList().AsReadOnly(),
                "Mesh ventilation on three sides. Removable fleece pad. Meets airline carry-on requirements.",
                "Carriers & Travel",
                "TravelPet",
                new[] { "airline-approved", "soft-sided", "portable" }.ToList().AsReadOnly(),
                ProductDimensions.Create(18, 11, 11, 2.5m)),

            // Bird Products
            Product.Create(
                "BIRD-CAGE-MED",
                "Medium Bird Cage with Stand",
                "Spacious cage for parakeets, cockatiels, and similar birds.",
                "Birds",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Bird+Cage", "Medium bird cage", 0)
                }.ToList().AsReadOnly(),
                "Includes perches, food dishes, and removable bottom tray for easy cleaning. Powder-coated finish.",
                "Cages & Habitats",
                "AvianHome",
                new[] { "cage", "stand", "medium" }.ToList().AsReadOnly(),
                ProductDimensions.Create(24, 18, 52, 28m)),

            Product.Create(
                "BIRD-SEED-MIX",
                "Premium Parakeet Seed Mix (5lb)",
                "Nutritious seed blend for parakeets and budgies.",
                "Birds",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Bird+Seed", "Parakeet seed mix", 0)
                }.ToList().AsReadOnly(),
                "Blend of millet, canary seed, and oats with added vitamins and minerals.",
                "Food & Treats",
                "AvianNutrition",
                new[] { "seed", "vitamins", "nutritious" }.ToList().AsReadOnly(),
                ProductDimensions.Create(10, 7, 3, 5m)),

            Product.Create(
                "BIRD-TOY-SWING",
                "Wooden Perch Swing with Bell",
                "Natural wood swing for mental stimulation and exercise.",
                "Birds",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Bird+Swing", "Wooden bird swing", 0)
                }.ToList().AsReadOnly(),
                "Made from natural wood. Includes brass bell for auditory stimulation.",
                "Toys & Enrichment",
                "FeatherFun",
                new[] { "natural-wood", "swing", "bell" }.ToList().AsReadOnly(),
                ProductDimensions.Create(6, 2, 8, 0.3m)),

            // Fish Products
            Product.Create(
                "FISH-TANK-20G",
                "20 Gallon Fish Tank (Includes Filter)",
                "Complete starter tank with LED lighting and filtration.",
                "Fish",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Fish+Tank", "20 gallon fish tank", 0)
                }.ToList().AsReadOnly(),
                "Includes LED hood, power filter, and heater. Perfect for tropical or freshwater setups.",
                "Aquariums & Stands",
                "AquaLife",
                new[] { "starter-kit", "filter", "led" }.ToList().AsReadOnly(),
                ProductDimensions.Create(24, 12, 16, 25m)),

            Product.Create(
                "FISH-FOOD-FLAKE",
                "Tropical Flake Food (4oz)",
                "Complete nutrition for tropical fish.",
                "Fish",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Fish+Food", "Tropical fish flakes", 0)
                }.ToList().AsReadOnly(),
                "Vitamin-enriched formula supports color and vitality. Will not cloud water.",
                "Food & Supplements",
                "AquaNutrition",
                new[] { "flakes", "vitamins", "tropical" }.ToList().AsReadOnly(),
                ProductDimensions.Create(3, 3, 5, 0.25m)),

            Product.Create(
                "FISH-DECOR-CAVE",
                "Aquarium Rock Cave Decoration",
                "Natural-looking hiding spot for fish.",
                "Fish",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Cave+Decor", "Aquarium cave", 0)
                }.ToList().AsReadOnly(),
                "Resin construction safe for all fish. Provides shelter and reduces stress.",
                "Decorations",
                "AquaDecor",
                new[] { "cave", "hiding-spot", "resin" }.ToList().AsReadOnly(),
                ProductDimensions.Create(5, 4, 3, 0.8m)),

            // Small Animal Products
            Product.Create(
                "HAMSTER-CAGE-LG",
                "Large Hamster Cage with Tubes",
                "Multi-level habitat with tunnels and exercise wheel.",
                "Small Animals",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Hamster+Cage", "Large hamster cage", 0)
                }.ToList().AsReadOnly(),
                "Features connecting tubes, water bottle, food dish, and silent spinner wheel.",
                "Cages & Habitats",
                "SmallPetHomes",
                new[] { "multi-level", "tubes", "wheel" }.ToList().AsReadOnly(),
                ProductDimensions.Create(24, 14, 12, 6m)),

            Product.Create(
                "RABBIT-HAY-5LB",
                "Timothy Hay for Rabbits (5lb)",
                "High-fiber hay for digestive health.",
                "Small Animals",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Timothy+Hay", "Timothy hay", 0)
                }.ToList().AsReadOnly(),
                "100% natural timothy hay. Essential for rabbits, guinea pigs, and chinchillas.",
                "Food & Hay",
                "SmallPetNutrition",
                new[] { "timothy-hay", "high-fiber", "natural" }.ToList().AsReadOnly(),
                ProductDimensions.Create(18, 12, 8, 5m)),

            Product.Create(
                "GUINEA-PIG-HIDEY",
                "Wooden Hideaway House",
                "Cozy wooden shelter for guinea pigs and rabbits.",
                "Small Animals",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Hideaway", "Wooden hideaway", 0)
                }.ToList().AsReadOnly(),
                "Made from natural wood. Provides security and chewing satisfaction.",
                "Accessories & Enrichment",
                "SmallPetFun",
                new[] { "wood", "hideaway", "natural" }.ToList().AsReadOnly(),
                ProductDimensions.Create(10, 8, 6, 1.5m)),

            // Reptile Products
            Product.Create(
                "REPTILE-TANK-40G",
                "40 Gallon Reptile Terrarium",
                "Glass terrarium with screen top for reptiles.",
                "Reptiles",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Terrarium", "40 gallon terrarium", 0)
                }.ToList().AsReadOnly(),
                "Front-opening doors for easy access. Screen top for ventilation. Perfect for bearded dragons, snakes, and more.",
                "Terrariums & Enclosures",
                "ReptileHome",
                new[] { "terrarium", "screen-top", "glass" }.ToList().AsReadOnly(),
                ProductDimensions.Create(36, 18, 18, 55m)),

            Product.Create(
                "REPTILE-LAMP-UVB",
                "UVB Basking Lamp (100W)",
                "Essential UVB lighting for reptile health.",
                "Reptiles",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=UVB+Lamp", "UVB basking lamp", 0)
                }.ToList().AsReadOnly(),
                "Provides necessary UVB rays for calcium absorption and vitamin D3 synthesis.",
                "Heating & Lighting",
                "ReptileLux",
                new[] { "uvb", "basking", "lamp" }.ToList().AsReadOnly(),
                ProductDimensions.Create(6, 6, 8, 0.5m)),

            Product.Create(
                "REPTILE-SUBSTRATE",
                "Desert Sand Substrate (10lb)",
                "Natural sand substrate for desert reptiles.",
                "Reptiles",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Sand+Substrate", "Desert sand", 0)
                }.ToList().AsReadOnly(),
                "Fine-grain sand ideal for bearded dragons and other desert species. Calcium-based.",
                "Substrates & Bedding",
                "ReptileGround",
                new[] { "sand", "substrate", "calcium" }.ToList().AsReadOnly(),
                ProductDimensions.Create(12, 8, 3, 10m)),

            // Multi-Pet Products
            Product.Create(
                "PET-GROOMING-KIT",
                "Professional Grooming Kit - All Pets",
                "Complete grooming set for dogs, cats, and small animals.",
                "Dogs",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Grooming+Kit", "Pet grooming kit", 0)
                }.ToList().AsReadOnly(),
                "Includes slicker brush, nail clippers, comb, and dematting tool. Storage case included.",
                "Grooming & Health",
                "GroomPro",
                new[] { "grooming", "brush", "clippers", "multi-pet" }.ToList().AsReadOnly(),
                ProductDimensions.Create(12, 8, 3, 1.8m)),

            Product.Create(
                "PET-GATE-WIDE",
                "Extra Wide Pet Gate (62 inch)",
                "Pressure-mounted safety gate for large openings.",
                "Dogs",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Pet+Gate", "Extra wide pet gate", 0)
                }.ToList().AsReadOnly(),
                "Walk-through design with one-handed operation. No tools required for installation.",
                "Safety & Containment",
                "SafeHome",
                new[] { "gate", "pressure-mount", "extra-wide" }.ToList().AsReadOnly(),
                ProductDimensions.Create(62, 2, 30, 8m)),

            Product.Create(
                "PET-CAMERA-WIFI",
                "WiFi Pet Camera with Treat Dispenser",
                "Monitor and interact with pets remotely.",
                "Dogs",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Pet+Camera", "WiFi pet camera", 0)
                }.ToList().AsReadOnly(),
                "1080p HD video, two-way audio, and remote treat tossing. Night vision included.",
                "Technology & Gadgets",
                "SmartPet",
                new[] { "camera", "wifi", "treat-dispenser", "hd" }.ToList().AsReadOnly(),
                ProductDimensions.Create(6, 6, 8, 1.2m)),

            Product.Create(
                "PET-FIRST-AID",
                "Pet First Aid Kit",
                "Complete emergency kit for all pets.",
                "Dogs",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=First+Aid", "Pet first aid kit", 0)
                }.ToList().AsReadOnly(),
                "Includes bandages, antiseptic wipes, tweezers, digital thermometer, and emergency guide.",
                "Health & Wellness",
                "SafePet",
                new[] { "first-aid", "emergency", "safety" }.ToList().AsReadOnly(),
                ProductDimensions.Create(8, 6, 3, 1m)),

            // Seasonal/Holiday Products
            Product.Create(
                "XMAS-PET-SWEATER",
                "Holiday Pet Sweater - Red & White",
                "Festive sweater for dogs and cats.",
                "Dogs",
                new[]
                {
                    ProductImage.Create("https://via.placeholder.com/400x400?text=Pet+Sweater", "Holiday pet sweater", 0)
                }.ToList().AsReadOnly(),
                "Soft knit fabric with snowflake pattern. Available in multiple sizes. Machine washable.",
                "Apparel & Accessories",
                "HolidayPaws",
                new[] { "holiday", "sweater", "seasonal" }.ToList().AsReadOnly(),
                ProductDimensions.Create(14, 10, 0.5m, 0.4m))
        };

        foreach (var product in products)
        {
            session.Store(product);
        }

        await session.SaveChangesAsync(ct);
    }
}
