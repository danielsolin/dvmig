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

         // Assert order: Base entities (non-system, non-activity) first, 
         // then Activities (non-system, activity)
         // Alphabetical within those groups.
         Assert.Equal("account", entities[0]);
         Assert.Equal("contact", entities[1]);
         
         // Activities should follow (alphabetical among activities)
         Assert.Equal("appointment", entities[2]);
         Assert.Equal("email", entities[3]);
         Assert.Equal("phonecall", entities[4]);
         Assert.Equal("task", entities[5]);

         Assert.Equal(6, entities.Count);
      }
   }
}
