using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HW.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_WordType_To_Vocab : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WordType",
                table: "Vocabs",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WordType",
                table: "Vocabs");
        }
    }
}
