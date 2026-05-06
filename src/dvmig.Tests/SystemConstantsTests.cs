using dvmig.Core.Shared;
using Xunit;
using System.Linq;

namespace dvmig.Tests
{
   public class SystemConstantsTests
   {
      [Fact]
      public void DataverseEntities_ToList_IncludesAllEntities()
      {
         // Act
         var entities = SystemConstants.DataverseEntities.ToList();

         // Assert
         Assert.Contains(entities, e => e.Name == "account");
         Assert.Contains(entities, e => e.Name == "contact");
         Assert.Contains(entities, e => e.Name == "task");
         Assert.Contains(entities, e => e.Name == "phonecall");
         Assert.Contains(entities, e => e.Name == "appointment");
         Assert.Contains(entities, e => e.Name == "email");
         Assert.Contains(entities, e => e.Name == "systemuser");
         Assert.Contains(entities, e => e.Name == "activityparty");
      }

      [Fact]
      public void RecommendedEntities_IncludesAppointment()
      {
         // Act
         var entities = SystemConstants.SyncSettings.RecommendedEntities;

         // Assert
         Assert.Contains("account", entities);
         Assert.Contains("contact", entities);
         Assert.Contains("task", entities);
         Assert.Contains("phonecall", entities);
         Assert.Contains("appointment", entities);
         Assert.Contains("email", entities);
         
         // Should not contain system entities
         Assert.DoesNotContain("systemuser", entities);
         Assert.DoesNotContain("activityparty", entities);
      }
      [Fact]
      public void RecommendedEntities_HasStableAndLogicalOrder()
      {
         // Act
         var entities = SystemConstants.SyncSettings.RecommendedEntities;

         // Assert order: Base entities first, then Activities
         Assert.Equal("account", entities[0]);
         Assert.Equal("contact", entities[1]);
         
         // Activities should follow (alphabetical among activities)
         Assert.Contains("appointment", entities.Skip(2));
         Assert.Contains("email", entities.Skip(2));
         Assert.Contains("phonecall", entities.Skip(2));
         Assert.Contains("task", entities.Skip(2));
      }
   }
}
