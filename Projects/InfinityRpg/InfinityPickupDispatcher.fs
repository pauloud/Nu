﻿namespace InfinityRpg
open System
open System.Numerics
open Prime
open Nu
open Nu.Declarative
open InfinityRpg

[<AutoOpen>]
module PickupDispatcher =

    type Entity with
        member this.GetPickup = this.GetModel<Pickup>
        member this.SetPickup = this.SetModel<Pickup>
        member this.Pickup = this.Model<Pickup> ()

    type PickupDispatcher () =
        inherit EntityDispatcher<Pickup, unit, unit> (Pickup.initial)

        static let getSpriteInsetOpt pickup =
            let spriteOffset =
                v2
                    (Constants.Layout.TileSize.X * single pickup.PickupSheetPositionM.X)
                    (Constants.Layout.TileSize.Y * single pickup.PickupSheetPositionM.Y)
            let spriteInset = v4Bounds spriteOffset Constants.Layout.TileSize
            Some spriteInset
        
        static member Properties =
            [define Entity.Depth Constants.Layout.PickupDepth
             define Entity.PublishChanges true
             define Entity.Omnipresent true]
        
        override this.Initializers (pickup, _) =
            [Entity.Position <== pickup --> fun pickup -> pickup.Position]
        
        override this.View (pickup, entity, world) =
            if entity.GetVisible world && entity.GetInView world then
                let transform = entity.GetTransform world
                [Render (transform.Depth, transform.Position.Y, AssetTag.generalize pickup.PickupSheet,
                     SpriteDescriptor
                       { Transform = transform
                         Offset = v2Zero
                         InsetOpt = getSpriteInsetOpt pickup
                         Image = pickup.PickupSheet
                         Color = Color.White
                         Glow = Color.Zero
                         Flip = FlipNone })]
            else []