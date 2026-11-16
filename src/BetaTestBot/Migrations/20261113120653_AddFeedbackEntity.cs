using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BetaTestBot.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedbackEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ScreenshotUrl",
                table: "Feedbacks",
                newName: "AttachmentUrl");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AttachmentUrl",
                table: "Feedbacks",
                newName: "ScreenshotUrl");
        }
    }
}
