using HW.Domain.Entities;
using HW.Domain.Enums;
using HW.Domain.ValueObjects;
using static HW.Application.Features.UserFitnessProfiles.Dtos.WorkoutPlanDtos;

namespace HW.Application.Features.UserFitnessProfiles;

internal static class WorkoutPlanRecipe
{
    // ──────────────────────────── Entry point ────────────────────────────

    public static GeneratedPlanResponseDto Generate(UserFitnessProfile profile)
    {
        var metrics = BodyMetrics.Compute(profile.PersonalInfo);
        var workout = BuildWorkoutPlan(profile);
        var meals = BuildMealPlan(profile, metrics);
        var grocery = BuildGroceryList(meals, profile.DietaryPreference);
        return new GeneratedPlanResponseDto(workout, meals, grocery);
    }

    // ──────────────────────────── Workout ────────────────────────────────

    private static readonly string[][] DaySplits = [
        [],
        [],
        [],
        ["Push", "Pull", "Legs"],
        ["Upper", "Lower", "Upper", "Lower"],
        ["Push", "Pull", "Legs", "Upper", "Core & Arms"],
        ["Push", "Pull", "Legs", "Push", "Pull", "Legs"],
    ];

    private static readonly string[] WeekDays = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    private record ExerciseTemplate(string Name, Equipment? Equip, string Notes);

    private static readonly Dictionary<string, ExerciseTemplate[]> ExerciseBank = new()
    {
        ["Push"] =
        [
            new("Barbell Bench Press",        Equipment.Barbell,    "Retract shoulder blades, controlled descent"),
            new("Incline Barbell Press",       Equipment.Barbell,    "45-degree incline for upper chest"),
            new("Overhead Barbell Press",      Equipment.Barbell,    "Brace core, bar path straight up"),
            new("Dumbbell Chest Press",        Equipment.Dumbbell,   "Full range, feel the stretch at bottom"),
            new("Dumbbell Shoulder Press",     Equipment.Dumbbell,   "Seated or standing, neutral grip"),
            new("Incline Dumbbell Press",      Equipment.Dumbbell,   "Control the eccentric phase"),
            new("Chest Press Machine",         Equipment.Machine,    "Adjust seat so handles align with mid-chest"),
            new("Shoulder Press Machine",      Equipment.Machine,    "Keep elbows at 90 degrees at bottom"),
            new("Pec Deck",                    Equipment.Machine,    "Slow and controlled, feel the squeeze"),
            new("Cable Crossover",             Equipment.Cable,      "Slight elbow bend, full contraction at cross"),
            new("Cable Lateral Raise",         Equipment.Cable,      "Raise to shoulder height, lead with elbow"),
            new("Push-up",                     Equipment.Bodyweight, "Chest to floor, full extension at top"),
            new("Pike Push-up",                Equipment.Bodyweight, "Hips high, targets shoulders"),
            new("Dip",                         Equipment.Bodyweight, "Lean forward for chest focus"),
            new("Kettlebell Press",            Equipment.Kettlebell, "Clean to rack position, press overhead"),
        ],
        ["Pull"] =
        [
            new("Barbell Row",                 Equipment.Barbell,    "Hinge at hips, row to belly button"),
            new("Deadlift",                    Equipment.Barbell,    "Neutral spine, drive through heels"),
            new("Dumbbell Row",                Equipment.Dumbbell,   "Support on bench, full stretch at bottom"),
            new("Dumbbell Pull-over",          Equipment.Dumbbell,   "Slight elbow bend, stretch the lats"),
            new("Lat Pulldown",                Equipment.Machine,    "Pull bar to upper chest, lean back slightly"),
            new("Seated Cable Row",            Equipment.Machine,    "Chest up, squeeze shoulder blades"),
            new("Cable Face Pull",             Equipment.Cable,      "Pull to forehead level, external rotation"),
            new("Straight-Arm Pulldown",       Equipment.Cable,      "Keep arms straight, engage lats"),
            new("Pull-up",                     Equipment.Bodyweight, "Full hang, chin over bar"),
            new("Chin-up",                     Equipment.Bodyweight, "Underhand grip, more bicep involvement"),
            new("Inverted Row",                Equipment.Bodyweight, "Adjust height to control difficulty"),
            new("Kettlebell Swing",            Equipment.Kettlebell, "Hip hinge, explosive hip drive, float at top"),
            new("Kettlebell Row",              Equipment.Kettlebell, "Single arm, brace on bench or knee"),
        ],
        ["Legs"] =
        [
            new("Barbell Back Squat",          Equipment.Barbell,    "Depth below parallel, knees track toes"),
            new("Barbell Romanian Deadlift",   Equipment.Barbell,    "Hip hinge, feel hamstring stretch"),
            new("Barbell Lunge",               Equipment.Barbell,    "Step forward, back knee near floor"),
            new("Goblet Squat",                Equipment.Dumbbell,   "Hold at chest, squat deep, elbows inside knees"),
            new("Dumbbell Lunge",              Equipment.Dumbbell,   "Alternate legs, maintain upright torso"),
            new("Dumbbell Romanian Deadlift",  Equipment.Dumbbell,   "Both hands, feel hamstrings load"),
            new("Leg Press",                   Equipment.Machine,    "Feet shoulder-width, full range of motion"),
            new("Leg Curl",                    Equipment.Machine,    "Curl slowly, hold contraction at top"),
            new("Leg Extension",               Equipment.Machine,    "Control the eccentric phase"),
            new("Calf Raise",                  Equipment.Machine,    "Full stretch at bottom, pause at top"),
            new("Cable Pull-Through",          Equipment.Cable,      "Hip hinge, glute focus"),
            new("Bodyweight Squat",            Equipment.Bodyweight, "Sit back and down, weight in heels"),
            new("Reverse Lunge",               Equipment.Bodyweight, "Knee-friendly, step back not forward"),
            new("Glute Bridge",                Equipment.Bodyweight, "Squeeze glutes at top, hold 2 seconds"),
            new("Step-up",                     Equipment.Bodyweight, "Drive through heel of elevated foot"),
            new("Kettlebell Goblet Squat",     Equipment.Kettlebell, "Deep squat, elbows inside knees"),
            new("Kettlebell Swing",            Equipment.Kettlebell, "Hip power, not arm lift"),
        ],
        ["Core & Arms"] =
        [
            new("Plank",                       Equipment.Bodyweight, "Hold 30–60 s, straight line head to heels"),
            new("Hanging Leg Raise",           Equipment.Bodyweight, "Keep legs straight, control the descent"),
            new("Russian Twist",               Equipment.Bodyweight, "Rotate fully, heels off floor"),
            new("Dead Bug",                    Equipment.Bodyweight, "Opposite arm-leg, lower back pressed flat"),
            new("Cable Crunch",                Equipment.Cable,      "Round spine down, elbows to knees"),
            new("Pallof Press",                Equipment.Cable,      "Resist rotation, hold at full extension 2 s"),
            new("Dumbbell Bicep Curl",         Equipment.Dumbbell,   "Supinate at top, control down"),
            new("Hammer Curl",                 Equipment.Dumbbell,   "Neutral grip, targets brachialis"),
            new("Tricep Overhead Extension",   Equipment.Dumbbell,   "Elbows in, lower behind head"),
            new("Tricep Pushdown",             Equipment.Cable,      "Elbows pinned, full extension"),
        ],
        ["Upper"] =
        [
            new("Barbell Bench Press",         Equipment.Barbell,    "Primary push — chest focus"),
            new("Barbell Row",                 Equipment.Barbell,    "Primary pull — back focus"),
            new("Overhead Barbell Press",      Equipment.Barbell,    "Shoulder compound"),
            new("Dumbbell Chest Press",        Equipment.Dumbbell,   "Full range, controlled"),
            new("Dumbbell Row",                Equipment.Dumbbell,   "Single arm, brace on bench"),
            new("Dumbbell Shoulder Press",     Equipment.Dumbbell,   "Seated, neutral or pronated grip"),
            new("Lat Pulldown",                Equipment.Machine,    "Wide grip, pull to chest"),
            new("Cable Crossover",             Equipment.Cable,      "Chest isolation"),
            new("Cable Face Pull",             Equipment.Cable,      "Rear delt and rotator cuff"),
            new("Pull-up",                     Equipment.Bodyweight, "Bodyweight pull compound"),
            new("Push-up",                     Equipment.Bodyweight, "Chest and tricep compound"),
        ],
    };

    private static List<DayWorkoutDto> BuildWorkoutPlan(UserFitnessProfile profile)
    {
        var days = profile.AvailableDaysPerWeek;
        var split = DaySplits[days];
        var (sets, reps, rest) = GetVolumeParams(profile.Goal, profile.FitnessLevel);
        var weight = GetWeightGuidance(profile.Goal, profile.FitnessLevel);

        return split.Select((category, i) =>
        {
            var exercises = PickExercises(category, profile.AvailableEquipment, profile.FitnessLevel);
            return new DayWorkoutDto(WeekDays[i], exercises
                .Select(e => new ExerciseDto(e.Name, sets, reps, weight, rest, e.Notes))
                .ToList());
        }).ToList();
    }

    private static List<ExerciseTemplate> PickExercises(
        string category, List<Equipment> availableEquipment, FitnessLevel level)
    {
        var bank = ExerciseBank.TryGetValue(category, out var pool) ? pool : ExerciseBank["Upper"];

        // Filter to exercises the user can actually do (no equipment requirement OR has that equipment)
        var matching = bank
            .Where(e => e.Equip is null || availableEquipment.Contains(e.Equip.Value))
            .ToList();

        // Fallback: if nothing matches, use bodyweight
        if (matching.Count == 0)
            matching = bank.Where(e => e.Equip == Equipment.Bodyweight).ToList();

        // Prioritise compound movements for beginners; allow more variation for advanced
        var exerciseCount = level switch
        {
            FitnessLevel.Beginner     => 4,
            FitnessLevel.Intermediate => 5,
            _                         => 6,
        };

        return matching.Take(exerciseCount).ToList();
    }

    private static (int sets, string reps, int restSeconds) GetVolumeParams(FitnessGoal goal, FitnessLevel level)
    {
        var levelBonus = level == FitnessLevel.Advanced ? 1 : 0;
        return goal switch
        {
            FitnessGoal.WeightLoss      => (3 + levelBonus, "15–20", 45),
            FitnessGoal.MuscleGain      => (4 + levelBonus, "8–12",  90),
            FitnessGoal.Endurance       => (3 + levelBonus, "20–25", 30),
            FitnessGoal.Maintenance     => (3 + levelBonus, "12–15", 60),
            _                           => (3 + levelBonus, "12–15", 60),
        };
    }

    private static string GetWeightGuidance(FitnessGoal goal, FitnessLevel level) =>
        (goal, level) switch
        {
            (FitnessGoal.MuscleGain,  FitnessLevel.Advanced)     => "80–85% 1RM",
            (FitnessGoal.MuscleGain,  FitnessLevel.Intermediate) => "70–75% 1RM",
            (FitnessGoal.MuscleGain,  _)                         => "60–65% 1RM",
            (FitnessGoal.WeightLoss,  _)                         => "50–60% 1RM",
            (FitnessGoal.Endurance,   _)                         => "40–50% 1RM",
            _                                                     => "60–70% 1RM",
        };

    // ──────────────────────────── Meals ──────────────────────────────────

    private record MealTemplate(string Name, int Calories, int Protein, int Carbs, int Fat);

    private static readonly Dictionary<DietaryPreference, (MealTemplate[] Breakfasts, MealTemplate[] Lunches, MealTemplate[] Dinners)> MealBank = new()
    {
        [DietaryPreference.None] = (
            [
                new("Oatmeal with Banana & Eggs",       420, 22, 60, 10),
                new("Scrambled Eggs on Whole-Grain Toast", 400, 28, 32, 14),
                new("Greek Yogurt Parfait with Granola", 380, 20, 52, 9),
                new("Protein Pancakes with Syrup",       440, 30, 50, 10),
                new("Avocado Toast with Poached Eggs",   430, 18, 38, 22),
                new("Cottage Cheese with Fruit",         350, 28, 38, 6),
                new("Whole-Grain Waffles with Berries",  400, 16, 60, 8),
            ],
            [
                new("Grilled Chicken & Brown Rice Bowl", 550, 48, 58, 10),
                new("Turkey & Avocado Wrap",             490, 38, 40, 16),
                new("Tuna Salad with Whole-Grain Bread", 470, 42, 38, 12),
                new("Beef & Vegetable Stir-Fry",         530, 40, 48, 14),
                new("Salmon Caesar Salad",               480, 40, 20, 24),
                new("Chicken Burrito Bowl",              560, 45, 55, 12),
                new("Egg Fried Rice",                    500, 28, 58, 14),
            ],
            [
                new("Grilled Salmon with Sweet Potato",  520, 44, 42, 16),
                new("Sirloin Steak with Roasted Veg",    600, 52, 30, 22),
                new("Chicken Stir-Fry with Noodles",     510, 42, 50, 10),
                new("Baked Cod with Quinoa & Broccoli",  480, 44, 44, 8),
                new("Pork Tenderloin with Mashed Potato",560, 46, 48, 14),
                new("Shrimp Pasta Primavera",            530, 36, 58, 12),
                new("Turkey Meatballs with Zucchini",    490, 44, 28, 18),
            ]),

        [DietaryPreference.Vegetarian] = (
            [
                new("Avocado Toast with Eggs",           430, 18, 38, 22),
                new("Greek Yogurt with Mixed Berries",   340, 20, 48, 6),
                new("Smoothie Bowl with Seeds",          380, 14, 62, 10),
                new("Veggie Omelette",                   360, 22, 14, 22),
                new("Cottage Cheese Fruit Bowl",         350, 28, 38, 6),
                new("Overnight Oats with Nuts",          420, 16, 58, 14),
                new("Whole-Grain Waffles with Fruit",    400, 14, 62, 8),
            ],
            [
                new("Caprese Salad with Quinoa",         420, 18, 50, 14),
                new("Veggie Wrap with Hummus",           440, 16, 56, 14),
                new("Quinoa Buddha Bowl",                480, 20, 62, 14),
                new("Lentil & Vegetable Soup",           400, 22, 56, 6),
                new("Cheese & Veggie Quesadilla",        460, 20, 48, 18),
                new("Falafel Pita with Tabbouleh",       490, 18, 58, 16),
                new("Tomato Basil Pasta",                500, 16, 72, 12),
            ],
            [
                new("Eggplant Parmesan",                 520, 20, 52, 22),
                new("Pasta Primavera",                   490, 18, 68, 14),
                new("Vegetable Curry with Rice",         480, 16, 72, 12),
                new("Black Bean Enchiladas",             520, 22, 62, 16),
                new("Mushroom Risotto",                  500, 14, 70, 16),
                new("Veggie Pizza on Whole-Grain Base",  540, 20, 66, 18),
                new("Tofu Scramble with Roasted Veg",    420, 24, 30, 18),
            ]),

        [DietaryPreference.Vegan] = (
            [
                new("Chia Pudding with Berries",         320, 10, 50, 10),
                new("Tofu Scramble with Veg",            360, 20, 30, 16),
                new("Smoothie Bowl with Hemp Seeds",     380, 14, 62, 10),
                new("Overnight Oats with Plant Milk",    400, 14, 60, 12),
                new("Avocado Toast with Tomato",         380, 10, 42, 20),
                new("Banana-Oat Pancakes",               380, 12, 64, 8),
                new("Peanut Butter Toast & Fruit",       420, 14, 58, 14),
            ],
            [
                new("Lentil Soup with Bread",            400, 22, 60, 6),
                new("Chickpea Buddha Bowl",              480, 20, 66, 12),
                new("Black Bean Tacos",                  460, 18, 60, 14),
                new("Tofu Fried Rice",                   490, 22, 62, 12),
                new("Tempeh Grain Bowl",                 510, 26, 58, 16),
                new("Veggie Noodle Stir-Fry",            440, 16, 62, 12),
                new("White Bean & Spinach Stew",         420, 20, 56, 8),
            ],
            [
                new("Tofu Stir-Fry with Brown Rice",     450, 24, 56, 12),
                new("Red Lentil Dal with Naan",          500, 22, 72, 10),
                new("Vegan Chili with Cornbread",        490, 22, 68, 10),
                new("Chickpea Tikka Masala",             480, 20, 64, 12),
                new("Stuffed Bell Peppers with Quinoa",  440, 18, 60, 10),
                new("Mushroom & Spinach Pasta",          470, 16, 66, 12),
                new("Cauliflower & Potato Curry",        420, 14, 62, 12),
            ]),

        [DietaryPreference.Keto] = (
            [
                new("Bacon & Eggs",                      450, 30, 2,  36),
                new("Cheese Omelette with Avocado",      500, 28, 4,  42),
                new("Bulletproof Coffee & Egg Muffins",  480, 24, 4,  40),
                new("Greek Yogurt with Nuts (full-fat)", 380, 22, 8,  28),
                new("Smoked Salmon with Cream Cheese",   420, 30, 2,  34),
                new("Avocado Egg Cups",                  360, 18, 4,  30),
                new("Almond Flour Pancakes",             400, 18, 8,  32),
            ],
            [
                new("Cobb Salad",                        520, 38, 6,  38),
                new("Lettuce-Wrapped Burger",            500, 36, 6,  34),
                new("Tuna-Stuffed Avocado",              480, 36, 6,  34),
                new("Chicken Caesar Salad (no croutons)",490, 42, 6,  30),
                new("Zucchini Noodles with Pesto",       440, 20, 10, 36),
                new("Egg Salad on Cucumber Slices",      380, 24, 4,  30),
                new("BLT Lettuce Wrap",                  400, 28, 4,  30),
            ],
            [
                new("Ribeye Steak & Asparagus",          620, 52, 6,  44),
                new("Salmon with Broccoli & Butter",     560, 46, 8,  38),
                new("Chicken Thighs with Roasted Veg",   540, 44, 10, 34),
                new("Pork Belly with Sauerkraut",        600, 40, 4,  48),
                new("Lamb Chops with Garlic Spinach",    580, 46, 6,  42),
                new("Shrimp Scampi with Zucchini Noodles", 480, 40, 8, 30),
                new("Ground Beef Stuffed Peppers",       500, 38, 10, 32),
            ]),

        [DietaryPreference.Paleo] = (
            [
                new("Egg & Sweet Potato Hash",           420, 24, 40, 16),
                new("Banana Almond-Butter Smoothie",     380, 14, 48, 16),
                new("Bacon & Veggie Frittata",           440, 28, 16, 28),
                new("Coconut Yogurt with Berries",       320, 8,  44, 12),
                new("Smoked Salmon & Avocado Plate",     400, 28, 10, 28),
                new("Paleo Granola with Coconut Milk",   400, 10, 46, 20),
                new("Hard-Boiled Eggs & Fruit",          320, 18, 32, 12),
            ],
            [
                new("Grilled Chicken Salad",             440, 40, 22, 20),
                new("Turkey Lettuce Wrap",               400, 34, 16, 20),
                new("Beef & Vegetable Soup",             460, 36, 30, 16),
                new("Salmon with Arugula",               480, 40, 14, 28),
                new("Shrimp & Veggie Stir-Fry",          420, 34, 26, 14),
                new("Chicken & Sweet Potato Bowl",       500, 40, 44, 12),
                new("Tuna with Cucumber & Olive",        380, 34, 10, 20),
            ],
            [
                new("Grilled Salmon with Roasted Veg",   520, 44, 24, 26),
                new("Grass-Fed Beef Patty with Salad",   560, 44, 14, 34),
                new("Pork Tenderloin with Apple Slaw",   500, 42, 28, 22),
                new("Chicken Thighs with Cauliflower",   520, 44, 20, 26),
                new("Lamb Kebabs with Grilled Veg",      540, 44, 22, 30),
                new("Baked Cod with Roasted Brussels",   460, 42, 22, 18),
                new("Turkey Meatballs with Zucchini",    480, 40, 20, 22),
            ]),

        [DietaryPreference.GlutenFree] = (
            [
                new("Gluten-Free Oats with Fruit",       400, 14, 62, 10),
                new("Scrambled Eggs with Potatoes",      420, 24, 38, 16),
                new("Rice Cakes with Almond Butter",     380, 12, 52, 14),
                new("Smoothie with Protein Powder",      380, 28, 44, 8),
                new("Quinoa Porridge with Berries",      400, 14, 62, 8),
                new("Greek Yogurt with GF Granola",      380, 20, 52, 9),
                new("Avocado Toast on GF Bread",         420, 14, 44, 20),
            ],
            [
                new("Rice Bowl with Grilled Chicken",    540, 46, 56, 10),
                new("GF Pasta Salad with Tuna",          490, 38, 50, 12),
                new("Quinoa Salad with Chickpeas",       460, 20, 60, 12),
                new("Corn Tortilla Tacos",               480, 30, 52, 14),
                new("Lettuce-Wrapped Turkey Burger",     440, 36, 20, 22),
                new("Thai Rice Noodle Salad",            460, 22, 60, 10),
                new("Stuffed Bell Peppers with Rice",    480, 26, 56, 12),
            ],
            [
                new("Grilled Salmon with Roasted Veg",   520, 44, 24, 26),
                new("Chicken & Rice Stir-Fry",           510, 44, 50, 10),
                new("Baked Cod with Quinoa",             480, 42, 42, 10),
                new("Sirloin with Sweet Potato",         580, 46, 44, 18),
                new("Shrimp & Grits (GF grits)",         520, 38, 52, 14),
                new("Chicken Fajita Bowl",               510, 44, 48, 12),
                new("Turkey Meatballs with GF Pasta",    500, 40, 52, 12),
            ]),
    };

    private static List<DayMealPlanDto> BuildMealPlan(UserFitnessProfile profile, BodyMetrics metrics)
    {
        var pref = MealBank.TryGetValue(profile.DietaryPreference, out var bank)
            ? bank
            : MealBank[DietaryPreference.None];

        var targetCalories = (int)CalculateTargetCalories(metrics.Bmr, profile.ActivityLevel, profile.Goal);

        return Enumerable.Range(0, 7).Select(i =>
        {
            var breakfast = pref.Breakfasts[i % pref.Breakfasts.Length];
            var lunch = pref.Lunches[i % pref.Lunches.Length];
            var dinner = pref.Dinners[i % pref.Dinners.Length];

            var totalCalories = ScaleCalories(breakfast.Calories + lunch.Calories + dinner.Calories, targetCalories);

            return new DayMealPlanDto(
                WeekDays[i],
                totalCalories,
                new MealDto(breakfast.Name, breakfast.Calories, breakfast.Protein, breakfast.Carbs, breakfast.Fat),
                new MealDto(lunch.Name,     lunch.Calories,     lunch.Protein,     lunch.Carbs,     lunch.Fat),
                new MealDto(dinner.Name,    dinner.Calories,    dinner.Protein,    dinner.Carbs,    dinner.Fat));
        }).ToList();
    }

    private static decimal CalculateTargetCalories(decimal bmr, ActivityLevel activity, FitnessGoal goal)
    {
        var tdee = bmr * activity switch
        {
            ActivityLevel.Sedentary         => 1.2m,
            ActivityLevel.LightlyActive     => 1.375m,
            ActivityLevel.ModeratelyActive  => 1.55m,
            ActivityLevel.VeryActive        => 1.725m,
            _                               => 1.9m,
        };

        return goal switch
        {
            FitnessGoal.WeightLoss  => tdee - 500,
            FitnessGoal.MuscleGain  => tdee + 300,
            FitnessGoal.Endurance   => tdee + 200,
            _                       => tdee,
        };
    }

    // Scale displayed total so it roughly reflects the user's calorie target
    private static int ScaleCalories(int templateTotal, int target) =>
        target > 0 ? target : templateTotal;

    // ──────────────────────────── Grocery list ───────────────────────────

    private static readonly Dictionary<DietaryPreference, GroceryListDto> GroceryTemplates = new()
    {
        [DietaryPreference.None] = new(
            Produce: ["Bananas", "Berries", "Broccoli", "Spinach", "Sweet Potato", "Bell Peppers", "Tomatoes", "Zucchini"],
            Protein: ["Chicken Breast", "Salmon Fillets", "Eggs", "Greek Yogurt", "Ground Turkey", "Sirloin Steak", "Tuna (canned)"],
            Grains:  ["Oats", "Brown Rice", "Whole-Grain Bread", "Quinoa", "Whole-Wheat Pasta"],
            Dairy:   ["Low-Fat Milk", "Cottage Cheese", "Cheddar Cheese"],
            Other:   ["Olive Oil", "Almonds", "Peanut Butter", "Honey", "Soy Sauce"]),

        [DietaryPreference.Vegetarian] = new(
            Produce: ["Avocados", "Berries", "Spinach", "Eggplant", "Bell Peppers", "Tomatoes", "Mushrooms", "Zucchini"],
            Protein: ["Eggs", "Greek Yogurt", "Cottage Cheese", "Tofu", "Lentils", "Chickpeas", "Mozzarella"],
            Grains:  ["Oats", "Brown Rice", "Whole-Grain Bread", "Quinoa", "Whole-Wheat Pasta"],
            Dairy:   ["Full-Fat Milk", "Parmesan", "Feta Cheese"],
            Other:   ["Olive Oil", "Hummus", "Walnuts", "Honey", "Tahini"]),

        [DietaryPreference.Vegan] = new(
            Produce: ["Bananas", "Berries", "Spinach", "Bell Peppers", "Sweet Potato", "Broccoli", "Cauliflower"],
            Protein: ["Tofu", "Tempeh", "Lentils", "Black Beans", "Chickpeas", "Hemp Seeds", "Edamame"],
            Grains:  ["Oats", "Brown Rice", "Quinoa", "Whole-Wheat Pasta", "Whole-Grain Bread"],
            Dairy:   ["Almond Milk", "Coconut Yogurt", "Cashew Cheese"],
            Other:   ["Olive Oil", "Nutritional Yeast", "Almond Butter", "Flaxseeds", "Maple Syrup"]),

        [DietaryPreference.Keto] = new(
            Produce: ["Avocados", "Spinach", "Broccoli", "Asparagus", "Zucchini", "Cauliflower", "Brussels Sprouts"],
            Protein: ["Bacon", "Eggs", "Salmon", "Ribeye Steak", "Chicken Thighs", "Ground Beef", "Shrimp"],
            Grains:  [],
            Dairy:   ["Heavy Cream", "Butter", "Cream Cheese", "Cheddar", "Parmesan"],
            Other:   ["Olive Oil", "Coconut Oil", "Almonds", "Walnuts", "MCT Oil"]),

        [DietaryPreference.Paleo] = new(
            Produce: ["Sweet Potato", "Berries", "Apples", "Spinach", "Broccoli", "Bell Peppers", "Plantains"],
            Protein: ["Grass-Fed Ground Beef", "Chicken Thighs", "Salmon", "Pork Tenderloin", "Eggs", "Turkey", "Shrimp"],
            Grains:  [],
            Dairy:   [],
            Other:   ["Coconut Oil", "Almond Butter", "Almonds", "Coconut Milk", "Balsamic Vinegar"]),

        [DietaryPreference.GlutenFree] = new(
            Produce: ["Berries", "Bananas", "Broccoli", "Bell Peppers", "Sweet Potato", "Avocados", "Spinach"],
            Protein: ["Chicken Breast", "Salmon", "Eggs", "Greek Yogurt", "Ground Turkey", "Tuna (canned)", "Shrimp"],
            Grains:  ["Certified GF Oats", "Brown Rice", "Quinoa", "Rice Noodles", "GF Bread", "Corn Tortillas"],
            Dairy:   ["Milk", "Cottage Cheese", "Cheddar"],
            Other:   ["Olive Oil", "Almond Butter", "GF Soy Sauce", "Honey", "Nuts"]),
    };

    private static GroceryListDto BuildGroceryList(
        List<DayMealPlanDto> _, DietaryPreference preference) =>
        GroceryTemplates.TryGetValue(preference, out var list)
            ? list
            : GroceryTemplates[DietaryPreference.None];
}
