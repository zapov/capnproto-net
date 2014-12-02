﻿using CapnProto.Schema.Parser;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Xunit;
using Xunit.Extensions;

namespace CapnProto.Schema
{
   // todo: move this somewhere else

   internal class CapnpParserTests
   {

      [Theory]
      [InlineData(@"
         @0xdbb9ad1f14bf0b36;  # unique file ID, generated by `capnp id`

         using Foo;
         using Foo.Bar;
         using Foo = Bar;
         using import ""foo.capnp"".Baz;
         using import ""foo.capnp"";
         using Foo = import ""x.c"";

         annotation baz(*) :Int32;

         $file;
         $file2(12);
         $x (); 

         const foo :Int32 = 123 $zz();
         const bar :Text = ""Hello"";
  
# Todo: references within consts      
# const baz :SomeStruct = (id = .foo, message = .bar);

         struct Person $foo(""bar"") { # blah
#blah

           using Foo.Bar;
           using T = Foo.Bar; 

           name @0 :Text;
           birthdate @3 :Date;

           enum Type $y {
             mobile @0;
             home @1 $blah;
             work @2;
           }

            # Anonymous union:
           union {
              foo @1 :Int32;
              baz @0 :import ""bar.capnp"".Baz;
           }

            # Various types with default values:
            foo @0 :Int32 = 123;
            bar @1 :Text = ""blah"";
            
            # Problem: Lits(Foobar) and we have not yet seen Foobar.
            list @0x1 :List(List(Text)) = [ [""foo"", ""bar""], [""baz""]];

            unknownType @0 :Foobar = ( idx = 123 );

            imaunion :union {
               foo @123 :Text = ""blah"";

               group_in_union :group {
                  width @2 :Int32;
               }
            }

            uselessgroup :group {
               
            }
            other_useless_group :group {
               foo @0 :Int32 = 1;
               bar @1 :Text $test( [ 123] );
            }

            interface Node {
               isDirectory @0 () -> (result :Bool);
            }
            interface Directory extends(Node) $x(1) {
              list @0 () -> (list :List(Entry));
              struct Entry {
                name @0 :Text;
                node @1 :Node;
              }

              create @1 (name :Text $boo) -> (file :File);
              mkdir @2 (name :Text) -> (directory :Directory);
              open @3 (name :Text) -> (node :Node);
              delete @4 (name :Text);
              link @5 (name :Text, node : Node);
            }
         }
      ")]
      public void Can_do_me_some_parsing(String source)
      {
         var parser = new CapnpParser(source);

         var result = parser.Parse();

         Trace.WriteLine(result);
      }

      [Theory]
      [InlineData(@"
         @0x1234;

         structFoobar{

         }
         struct Foobar{}
      
      ")]
      public void Fail_at_bad_input(String badSource)
      {
         try
         {
            var parser = new CapnpParser(badSource);
            var result = parser.Parse();
            Assert.True(false);
         }
         catch (Xunit.Sdk.AssertException) { throw; }
         catch (Exception e)
         {
            // OK, for now
            Trace.WriteLine(e.Message);
         }
      }

      [Fact]
      public void Can_parse_schema_capnp()
      {
         var schemaFile = @"..\..\..\Tests\Schema\schema.capnp";

         var schemaContent = File.ReadAllText(schemaFile);

         var parser = new CapnpParser(schemaContent);

         var module = parser.Parse();
         module = parser.ProcessParsedSource(module, s =>
         {
            if (s == "/capnp/c++.capnp")
               return @"
                  @0xbdf87d7bb8304e81;
                  $namespace(""capnp::annotations"");
                  annotation namespace(file): Text;
                  annotation name(field, enumerant, struct, enum, interface, method, param, group, union): Text;
               ";
            throw new Exception("don't understand schema.capnp import " + s);
         });


         Trace.WriteLine(module.ToString());
      }

      [Theory]
      [InlineData(@"
         @0x1234;

         struct Foobar {
            Foo @0 :import ""foo.capnp"".Bar;
         }


      ", new[] { "foo.capnp", @" 
         @0x99;
         struct Bar {}
      "})]
      public void Can_resolve_imports(String source, String[] imports)
      {
         Func<String, String> getImport = s =>
         {
            for (var i = 0; i < imports.Length; i++)
               if (imports[i] == s) return imports[i + 1];
            throw new Exception("could not find " + s);
         };

         var parser = new CapnpParser(source);
         var phase1 = parser.Parse();

         var phase2 = parser.ProcessParsedSource(phase1, getImport);
      }

      [Theory]
      [InlineData(@"
      @0x123;

      struct Foobar {
         ref1 @0 : Bar;

         struct Bar {}
      }

      struct OuterTest {
         ref2 @0: Baz;
      }

      struct Baz {}

   
      ")]
      public void Can_resolve_references(String source)
      {
         var parser = new CapnpParser(source);
         var module = parser.Parse();
         module = parser.ProcessParsedSource(module, null);


      }

      [Theory]
      [InlineData(@"
      @123;

      struct Foobar
      {
         using TT = Bar;

         struct Bar{}

         usingRef @0 :TT;

         unresolvedRawData @1: LaterDef = ( Foo = ""blah"", Bar = 123 );
      }

      struct LaterDef {
          Foo @0: Text;
          Bar @1: Int32;
      }

      struct AA
      {
#     using TA = import ""foo.capnp"".BB;
#         a @0 :TA;
      }

      ", new[] { "foo.capnp", @" 
         @0x99;
         struct Bar {}

         struct BB {
#    using TB = import ""::global"".AA;
#      b @0 :TB;
         }
      "})]
      public void Can_fully_parse_source(String source, String[] imports)
      {
         Func<String, String> getImport = s =>
         {
            for (var i = 0; i < imports.Length; i++)
               if (imports[i] == s) return imports[i + 1];
            if (s == "::global") return source;
            throw new Exception("could not find " + s);
         };

         var parser = new CapnpParser(source);
         var phase1 = parser.Parse();

         var phase2 = parser.ProcessParsedSource(phase1, getImport);


      }

      // To test are things like:
      // - construct a .capnp file that contains the entire syntax
      // - circular imports
      // - circular references due to imports etc. etc. etc.


      [Fact]
      public void Can_handle_entire_capnp_syntax()
      {
         var source = File.ReadAllText("..\\..\\Parser\\FullSyntax.capnp");
         var p = new CapnpParser(source);
         var m = p.Parse();
         m = p.ProcessParsedSource(m, null);
      }

      [Theory]
      [InlineData(@"
         # No id. 
      ")]
      [InlineData(@"
         @123;
         struct Singles {
            text @0: Text = 'foobar';
         }
      ")]
      [InlineData("@123; struct Hex { hex @0: Int32 = 0X123; }")]
      public void Detect_bad_syntax(String source)
      {
         var p = new CapnpParser(source);

         try
         {
            p.ProcessParsedSource(p.Parse(), null);
         }
         catch
         {
            // todo: filter on exception later
            // OK
         }
      }

      [Fact]
      public void Can_parse_ints()
      {
         Int32 i;
         Assert.True(NumberParser<Int32>.TryParse("3", NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out i));
         Assert.Equal(3, i);

         Assert.Equal(UInt32.MaxValue, NumberParser<UInt32>.Max);

         Assert.True(NumberParser<Int32>.TryParseOctal("123", out i));
         Assert.Equal(Convert.ToInt32("123", 8), i);

         Assert.True(NumberParser<Int32>.TryParseOctal("17777777777", out i));
         Assert.Equal(Int32.MaxValue, i);

         Assert.False(NumberParser<Int32>.TryParseOctal("17777777778", out i));

         Double d;
         Assert.True(NumberParser<Double>.TryParse("-3.1415", NumberStyles.Float, NumberFormatInfo.InvariantInfo, out d));
         Assert.Equal(-3.1415, d);
      }
   }
}
