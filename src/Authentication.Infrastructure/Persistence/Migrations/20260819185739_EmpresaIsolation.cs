using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Authentication.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmpresaIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_TenantId_Name",
                table: "Products");

            migrationBuilder.AddColumn<Guid>(
                name: "EmpresaId",
                table: "Products",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Products_EmpresaId",
                table: "Products",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_EmpresaId_Name",
                table: "Products",
                columns: new[] { "TenantId", "EmpresaId", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Empresas_EmpresaId",
                table: "Products",
                column: "EmpresaId",
                principalTable: "Empresas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Empresas_EmpresaId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_EmpresaId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_TenantId_EmpresaId_Name",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "Products");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_Name",
                table: "Products",
                columns: new[] { "TenantId", "Name" });
        }
    }
}
