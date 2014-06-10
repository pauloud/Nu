﻿namespace Nu
open System
open System.Collections.Generic
open System.ComponentModel
open System.Reflection
open System.Xml
open System.Xml.Serialization
open FSharpx
open FSharpx.Lens.Operators
open OpenTK
open TiledSharp
open Prime
open Nu
open Nu.NuConstants

[<AutoOpen>]
module EntityModule =

    type Entity with

        [<XField>] member this.Position with get () = this?Position () : Vector2
        member this.SetPosition (value : Vector2) : Entity = this?Position <- value
        [<XField>] member this.Depth with get () = this?Depth () : single
        member this.SetDepth (value : single) : Entity = this?Depth <- value
        [<XField>] member this.Rotation with get () = this?Rotation () : single
        member this.SetRotation (value : single) : Entity = this?Rotation <- value
        [<XField>] member this.Size with get () = this?Size () : Vector2
        member this.SetSize (value : Vector2) : Entity = this?Size <- value

        member this.Init (dispatcherContainer : IXDispatcherContainer) : Entity = this?Init dispatcherContainer
        member this.Register (address : Address, world : World) : Entity * World = this?Register (address, world)
        member this.Unregister (address : Address, world : World) : World = this?Unregister (address, world)
        member this.PropagatePhysics (address : Address, world : World) : World = this?PropagatePhysics (address, world)
        member this.ReregisterPhysicsHack (address : Address, world : World) : World = this?ReregisterPhysicsHack (address, world)
        member this.HandleBodyTransformMessage (message : BodyTransformMessage, address : Address, world : World) : World = this?HandleBodyTransformMessage (message, address, world)
        member this.GetRenderDescriptors (viewAbsolute : Matrix3, viewRelative : Matrix3, world : World) : RenderDescriptor list = this?GetRenderDescriptors (viewAbsolute, viewRelative, world)
        member this.GetQuickSize (world : World) : Vector2 = this?GetQuickSize world
        member this.IsTransformRelative (world : World) : bool = this?IsTransformRelative world

[<RequireQualifiedAccess>]
module Entity =

    let mouseToEntity (position : Vector2) world (entity : Entity) =
        let positionScreen = Camera.mouseToScreen position world.Camera
        let view = (if entity.IsTransformRelative world then Camera.getViewRelativeF else Camera.getViewAbsoluteF) world.Camera
        let positionEntity = positionScreen * view
        positionEntity

    let setPositionSnapped snap position (entity : Entity) =
        let position' = NuMath.snap2F snap position
        entity.SetPosition position'

    let getTransform (entity : Entity) =
        { Transform.Position = entity.Position
          Depth = entity.Depth
          Size = entity.Size
          Rotation = entity.Rotation }

    let setTransform positionSnap rotationSnap transform (entity : Entity) =
        let transform' = NuMath.snapTransform positionSnap rotationSnap transform
        entity
            .SetPosition(transform'.Position)
            .SetDepth(transform'.Depth)
            .SetSize(transform'.Size)
            .SetRotation(transform'.Rotation)

    let getPickingPriority (entity : Entity) =
        entity.Depth

    let private makeDefault2 defaultDispatcherName optName =
        let id = NuCore.getId ()
        { Id = id
          Name = match optName with None -> string id | Some name -> name
          Enabled = true
          Visible = true
          FacetNamesNs = []
          Xtension = { XFields = Map.empty; OptXDispatcherName = Some defaultDispatcherName; CanDefault = true; Sealed = false }}

    let makeDefault defaultDispatcherName optName seal (dispatcherContainer : IXDispatcherContainer) =
        match Map.tryFind defaultDispatcherName <| dispatcherContainer.GetDispatchers () with
        | None -> failwith <| "Invalid XDispatcher name '" + defaultDispatcherName + "'."
        | Some dispatcher ->
            let entity = makeDefault2 defaultDispatcherName optName
            let entity' = entity.Init dispatcherContainer
            { entity' with Xtension = { entity'.Xtension with Sealed = seal }}

    let writeToXml (writer : XmlWriter) entity =
        writer.WriteStartElement typeof<Entity>.Name
        Xtension.writePropertiesToXmlWriter writer entity
        writer.WriteEndElement ()

    let writeManyToXml (writer : XmlWriter) (entities : Map<string, Entity>) =
        for entityKvp in entities do
            writeToXml writer entityKvp.Value

    let readFromXml (entityNode : XmlNode) defaultDispatcherName seal (world : World) =
        let entity = makeDefault defaultDispatcherName None seal world
        Xtension.readProperties entityNode entity
        entity

    let readManyFromXml (parentNode : XmlNode) defaultDispatcherName seal world =
        let entityNodes = parentNode.SelectNodes "Entity"
        let entities =
            Seq.map
                (fun entityNode -> readFromXml entityNode defaultDispatcherName seal world)
                (enumerable entityNodes)
        Seq.toList entities