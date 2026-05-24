using static dvmig.Core.Shared.SystemConstants;


namespace dvmig.Tests
{
   public class SystemConstantsTests
   {
      [Fact]
      public void DataverseEntities_ToList_IncludesAllEntities()
      {
         var entities = DataverseEntities.ToList();

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
         var entities = SyncSettings.RecommendedEntities;

         Assert.Contains("account", entities);
         Assert.Contains("contact", entities);
         Assert.Contains("task", entities);
         Assert.Contains("phonecall", entities);
         Assert.Contains("appointment", entities);
         Assert.Contains("email", entities);

         Assert.DoesNotContain("systemuser", entities);
         Assert.DoesNotContain("activityparty", entities);
      }

      [Fact]
      public void RecommendedEntities_HasStableAndLogicalOrder()
      {
         var entities = SyncSettings.RecommendedEntities;

         Assert.Equal("account", entities[0]);
         Assert.Equal("contact", entities[1]);

         Assert.Equal("appointment", entities[2]);
         Assert.Equal("email", entities[3]);
         Assert.Equal("phonecall", entities[4]);
         Assert.Equal("task", entities[5]);

         Assert.Equal(6, entities.Count);
      }

      [Fact]
      public void DefaultVisibleEntities_ExtendsRecommendedEntities()
      {
         var entities = SyncSettings.DefaultVisibleEntities;

         Assert.Contains("account", entities);
         Assert.Contains("contact", entities);
         Assert.Contains("activitymimeattachment", entities);
         Assert.Contains("annotation", entities);
         Assert.Contains("attachment", entities);
         Assert.Contains("businessunit", entities);
         Assert.Contains("connection", entities);
         Assert.Contains("competitor", entities);
         Assert.Contains("letter", entities);
         Assert.Contains("lead", entities);
         Assert.Contains("subject", entities);
         Assert.Contains("team", entities);
         Assert.Contains("territory", entities);
         Assert.Contains("transactioncurrency", entities);
         Assert.Contains("uom", entities);
         Assert.Contains("uomschedule", entities);
         Assert.Contains("opportunity", entities);
         Assert.Contains("quote", entities);
         Assert.Contains("salesorder", entities);
         Assert.Contains("invoice", entities);
         Assert.Contains("product", entities);
         Assert.Contains("incident", entities);
         Assert.Contains("campaign", entities);

         Assert.DoesNotContain("systemuser", entities);
         Assert.DoesNotContain("activityparty", entities);
         Assert.DoesNotContain("queue", entities);
         Assert.DoesNotContain("template", entities);
      }
   }
}
