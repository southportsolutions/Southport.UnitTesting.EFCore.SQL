using AutoBogus;
using Southport.UnitTesting.EFCore.SQL.Tests.Database;

namespace Southport.UnitTesting.EFCore.SQL.Tests.Fakes
{
    public class FakeTestEntity : AutoFaker<TestEntity>
    {
        public FakeTestEntity()
        {
            RuleFor(f => f.Id, () => 0);
        }
    }
}
