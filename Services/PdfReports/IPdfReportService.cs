using RowadUmrahSystem.Web.Models;

namespace RowadUmrahSystem.Web.Services.PdfReports
{
    public interface IPdfReportService
    {
        byte[] GenerateTravelerProfile(Models.Traveler traveler);
        byte[] GenerateBlockedTravelersReport(List<Traveler> travelers);
        byte[] GenerateTripsReport(List<Trip> trips);
        byte[] GenerateTravelersReport(List<Traveler> travelers);
        byte[] GenerateDeletedTravelersReport(List<Traveler> travelers);
        byte[] GenerateAuditLogsReport(List<AuditLog> logs);
        byte[] GenerateDocumentsReport(List<TravelerDocument> documents);
        byte[] GenerateDeletedDocumentsReport(List<TravelerDocument> documents);
        byte[] GenerateExpiringPassportsReport(List<Traveler> travelers);
        byte[] GenerateExpiredPassportsReport(List<Traveler> travelers);

    }
}