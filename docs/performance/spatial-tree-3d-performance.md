# 3D Spatial Tree Performance Benchmarks

## TL;DR: What Problem This Solves

- Need fast “what’s near X?” or “what’s inside this volume?” in 3D.
- These structures avoid scanning every object; queries touch only nearby data.
- Quick picks: OctTree3D for general 3D queries; KdTree3D for nearest‑neighbor on points; RTree3D for volumetric bounds.

Note: KdTree3D, OctTree3D, and RTree3D are under active development and their APIs/performance may evolve. SpatialHash3D is stable and recommended for broad‑phase neighbor queries with many moving objects.

For boundary and result semantics across structures, see [Spatial Tree Semantics](../features/spatial/spatial-tree-semantics.md)

This document contains performance benchmarks for the 3D spatial tree implementations in Unity Helpers.

## Available 3D Spatial Trees

- **OctTree3D** - Easiest to use, good all-around performance for 3D
- **KdTree3D** - Balanced and unbalanced variants available
- **RTree3D** - Optimized for 3D bounding box queries
- **SpatialHash3D** - Efficient for uniformly distributed moving objects (stable)

## Performance Benchmarks

<!-- SPATIAL_TREE_3D_BENCHMARKS_START -->

### Datasets

<!-- tabs:start -->

#### **1,000,000 entries**

##### Construction

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Construction</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">1,000,000 entries</td><td align="right">2 (0.340s)</td><td align="right">5 (0.193s)</td><td align="right">2 (0.443s)</td><td align="right">1 (0.560s)</td></tr>
  </tbody>
</table>

##### Elements In Range

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Elements In Range</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (~span/2) (r=49.50)</td><td align="right">30</td><td align="right">33</td><td align="right">32</td><td align="right">14</td></tr>
    <tr><td align="left">Half (~span/4) (r=24.75)</td><td align="right">258</td><td align="right">288</td><td align="right">251</td><td align="right">161</td></tr>
    <tr><td align="left">Quarter (~span/8) (r=12.38)</td><td align="right">1,878</td><td align="right">2,274</td><td align="right">1,747</td><td align="right">1,567</td></tr>
    <tr><td align="left">Tiny (~span/1000) (r=1)</td><td align="right">68,467</td><td align="right">68,902</td><td align="right">140,371</td><td align="right">70,584</td></tr>
  </tbody>
</table>

##### Get Elements In Bounds

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Get Elements In Bounds</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (size≈99.00x99.00x99.00)</td><td align="right">32</td><td align="right">35</td><td align="right">197</td><td align="right">18</td></tr>
    <tr><td align="left">Half (size≈49.50x49.50x49.50)</td><td align="right">44</td><td align="right">49</td><td align="right">1,121</td><td align="right">279</td></tr>
    <tr><td align="left">Quarter (size≈24.75x24.75x24.75)</td><td align="right">45</td><td align="right">52</td><td align="right">3,249</td><td align="right">2,901</td></tr>
    <tr><td align="left">Unit (size=1)</td><td align="right">48</td><td align="right">53</td><td align="right">146,797</td><td align="right">72,976</td></tr>
  </tbody>
</table>

##### Approximate Nearest Neighbors

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Approximate Nearest Neighbors</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">500 neighbors</td><td align="right">14,952</td><td align="right">31,212</td><td align="right">2,559</td><td align="right">1,285</td></tr>
    <tr><td align="left">100 neighbors</td><td align="right">158,435</td><td align="right">175,623</td><td align="right">12,733</td><td align="right">6,641</td></tr>
    <tr><td align="left">10 neighbors</td><td align="right">488,977</td><td align="right">323,353</td><td align="right">18,726</td><td align="right">9,150</td></tr>
    <tr><td align="left">1 neighbor</td><td align="right">555,407</td><td align="right">278,327</td><td align="right">22,921</td><td align="right">9,414</td></tr>
  </tbody>
</table>

#### **100,000 entries**

##### Construction

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Construction</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">100,000 entries</td><td align="right">42 (0.024s)</td><td align="right">80 (0.012s)</td><td align="right">56 (0.018s)</td><td align="right">27 (0.036s)</td></tr>
  </tbody>
</table>

##### Elements In Range

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Elements In Range</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (~span/2) (r=49.50)</td><td align="right">412</td><td align="right">489</td><td align="right">642</td><td align="right">213</td></tr>
    <tr><td align="left">Half (~span/4) (r=24.75)</td><td align="right">1,464</td><td align="right">1,876</td><td align="right">1,920</td><td align="right">836</td></tr>
    <tr><td align="left">Quarter (~span/8) (r=12.38)</td><td align="right">4,746</td><td align="right">7,000</td><td align="right">6,304</td><td align="right">3,454</td></tr>
    <tr><td align="left">Tiny (~span/1000) (r=1)</td><td align="right">74,640</td><td align="right">83,016</td><td align="right">182,424</td><td align="right">94,286</td></tr>
  </tbody>
</table>

##### Get Elements In Bounds

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Get Elements In Bounds</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (size≈99.00x99.00x9)</td><td align="right">587</td><td align="right">726</td><td align="right">2,580</td><td align="right">399</td></tr>
    <tr><td align="left">Half (size≈49.50x49.50x4.5)</td><td align="right">673</td><td align="right">847</td><td align="right">8,002</td><td align="right">4,047</td></tr>
    <tr><td align="left">Quarter (size≈24.75x24.75x2.25)</td><td align="right">683</td><td align="right">855</td><td align="right">38,898</td><td align="right">27,884</td></tr>
    <tr><td align="left">Unit (size=1)</td><td align="right">706</td><td align="right">836</td><td align="right">190,116</td><td align="right">96,833</td></tr>
  </tbody>
</table>

##### Approximate Nearest Neighbors

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Approximate Nearest Neighbors</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">500 neighbors</td><td align="right">22,842</td><td align="right">37,790</td><td align="right">1,826</td><td align="right">1,170</td></tr>
    <tr><td align="left">100 neighbors</td><td align="right">111,561</td><td align="right">118,529</td><td align="right">10,595</td><td align="right">4,356</td></tr>
    <tr><td align="left">10 neighbors</td><td align="right">548,563</td><td align="right">443,954</td><td align="right">22,855</td><td align="right">8,878</td></tr>
    <tr><td align="left">1 neighbor</td><td align="right">553,853</td><td align="right">392,851</td><td align="right">35,431</td><td align="right">13,695</td></tr>
  </tbody>
</table>

#### **10,000 entries**

##### Construction

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Construction</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">10,000 entries</td><td align="right">519 (0.002s)</td><td align="right">596 (0.002s)</td><td align="right">545 (0.002s)</td><td align="right">320 (0.003s)</td></tr>
  </tbody>
</table>

##### Elements In Range

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Elements In Range</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (~span/2) (r=49.50)</td><td align="right">4,295</td><td align="right">4,316</td><td align="right">6,192</td><td align="right">2,171</td></tr>
    <tr><td align="left">Half (~span/4) (r=24.75)</td><td align="right">7,371</td><td align="right">7,900</td><td align="right">7,999</td><td align="right">4,246</td></tr>
    <tr><td align="left">Quarter (~span/8) (r=12.38)</td><td align="right">10,441</td><td align="right">12,003</td><td align="right">12,626</td><td align="right">7,274</td></tr>
    <tr><td align="left">Tiny (~span/1000) (r=1)</td><td align="right">116,107</td><td align="right">110,829</td><td align="right">240,202</td><td align="right">143,509</td></tr>
  </tbody>
</table>

##### Get Elements In Bounds

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Get Elements In Bounds</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (size≈99.00x9x9)</td><td align="right">6,021</td><td align="right">5,941</td><td align="right">24,715</td><td align="right">4,112</td></tr>
    <tr><td align="left">Half (size≈49.50x4.5x4.5)</td><td align="right">6,889</td><td align="right">6,760</td><td align="right">37,847</td><td align="right">42,414</td></tr>
    <tr><td align="left">Quarter (size≈24.75x2.25x2.25)</td><td align="right">6,869</td><td align="right">6,894</td><td align="right">136,372</td><td align="right">118,433</td></tr>
    <tr><td align="left">Unit (size=1)</td><td align="right">6,944</td><td align="right">7,020</td><td align="right">261,198</td><td align="right">148,865</td></tr>
  </tbody>
</table>

##### Approximate Nearest Neighbors

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Approximate Nearest Neighbors</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">500 neighbors</td><td align="right">28,062</td><td align="right">29,396</td><td align="right">638</td><td align="right">858</td></tr>
    <tr><td align="left">100 neighbors</td><td align="right">130,380</td><td align="right">170,968</td><td align="right">6,926</td><td align="right">5,238</td></tr>
    <tr><td align="left">10 neighbors</td><td align="right">494,527</td><td align="right">428,416</td><td align="right">35,432</td><td align="right">16,135</td></tr>
    <tr><td align="left">1 neighbor</td><td align="right">608,641</td><td align="right">604,545</td><td align="right">55,460</td><td align="right">25,402</td></tr>
  </tbody>
</table>

#### **1,000 entries**

##### Construction

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Construction</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">1,000 entries</td><td align="right">4,191 (0.000s)</td><td align="right">5,927 (0.000s)</td><td align="right">3,631 (0.000s)</td><td align="right">3,231 (0.000s)</td></tr>
  </tbody>
</table>

##### Elements In Range

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Elements In Range</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (~span/2) (r=4.5)</td><td align="right">28,247</td><td align="right">31,777</td><td align="right">29,009</td><td align="right">22,421</td></tr>
    <tr><td align="left">Half (~span/4) (r=2.25)</td><td align="right">141,701</td><td align="right">167,956</td><td align="right">149,245</td><td align="right">138,145</td></tr>
    <tr><td align="left">Quarter (~span/8) (r=1.13)</td><td align="right">171,864</td><td align="right">177,662</td><td align="right">358,881</td><td align="right">199,969</td></tr>
    <tr><td align="left">Tiny (~span/1000) (r=1)</td><td align="right">171,026</td><td align="right">175,651</td><td align="right">358,054</td><td align="right">197,144</td></tr>
  </tbody>
</table>

##### Get Elements In Bounds

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Get Elements In Bounds</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (size≈9x9x9)</td><td align="right">55,367</td><td align="right">60,588</td><td align="right">231,924</td><td align="right">42,034</td></tr>
    <tr><td align="left">Half (size≈4.5x4.5x4.5)</td><td align="right">60,826</td><td align="right">64,675</td><td align="right">158,172</td><td align="right">166,422</td></tr>
    <tr><td align="left">Quarter (size≈2.25x2.25x2.25)</td><td align="right">59,881</td><td align="right">65,929</td><td align="right">383,738</td><td align="right">208,592</td></tr>
    <tr><td align="left">Unit (size=1)</td><td align="right">61,340</td><td align="right">67,222</td><td align="right">383,240</td><td align="right">209,228</td></tr>
  </tbody>
</table>

##### Approximate Nearest Neighbors

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Approximate Nearest Neighbors</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">500 neighbors</td><td align="right">34,915</td><td align="right">37,952</td><td align="right">3,499</td><td align="right">2,843</td></tr>
    <tr><td align="left">100 neighbors</td><td align="right">153,489</td><td align="right">165,612</td><td align="right">18,697</td><td align="right">13,630</td></tr>
    <tr><td align="left">10 neighbors</td><td align="right">536,964</td><td align="right">398,357</td><td align="right">93,470</td><td align="right">44,258</td></tr>
    <tr><td align="left">1 neighbor</td><td align="right">640,037</td><td align="right">650,403</td><td align="right">104,003</td><td align="right">55,357</td></tr>
  </tbody>
</table>

#### **100 entries**

##### Construction

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Construction</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">100 entries</td><td align="right">38,759 (0.000s)</td><td align="right">10,030 (0.000s)</td><td align="right">24,213 (0.000s)</td><td align="right">15,698 (0.000s)</td></tr>
  </tbody>
</table>

##### Elements In Range

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Elements In Range</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (~span/2) (r=4.5)</td><td align="right">266,801</td><td align="right">266,250</td><td align="right">302,534</td><td align="right">192,410</td></tr>
    <tr><td align="left">Half (~span/4) (r=2.25)</td><td align="right">341,789</td><td align="right">349,460</td><td align="right">356,948</td><td align="right">281,973</td></tr>
    <tr><td align="left">Quarter (~span/8) (r=1.13)</td><td align="right">346,927</td><td align="right">326,452</td><td align="right">425,838</td><td align="right">359,988</td></tr>
    <tr><td align="left">Tiny (~span/1000) (r=1)</td><td align="right">340,885</td><td align="right">355,813</td><td align="right">433,093</td><td align="right">358,502</td></tr>
  </tbody>
</table>

##### Get Elements In Bounds

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Get Elements In Bounds</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (size≈9x4x1)</td><td align="right">479,348</td><td align="right">475,542</td><td align="right">1,205,263</td><td align="right">322,799</td></tr>
    <tr><td align="left">Half (size≈4.5x2x1)</td><td align="right">491,498</td><td align="right">501,104</td><td align="right">391,708</td><td align="right">381,900</td></tr>
    <tr><td align="left">Quarter (size≈2.25x1x1)</td><td align="right">498,882</td><td align="right">495,579</td><td align="right">549,227</td><td align="right">536,053</td></tr>
    <tr><td align="left">Unit (size=1)</td><td align="right">491,599</td><td align="right">486,230</td><td align="right">548,382</td><td align="right">528,585</td></tr>
  </tbody>
</table>

##### Approximate Nearest Neighbors

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Approximate Nearest Neighbors</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">100 neighbors (max)</td><td align="right">192,205</td><td align="right">196,614</td><td align="right">109,799</td><td align="right">155,845</td></tr>
    <tr><td align="left">10 neighbors</td><td align="right">615,894</td><td align="right">563,599</td><td align="right">139,741</td><td align="right">217,289</td></tr>
    <tr><td align="left">1 neighbor</td><td align="right">625,397</td><td align="right">641,929</td><td align="right">212,152</td><td align="right">352,882</td></tr>
  </tbody>
</table>
<!-- tabs:end -->
<!-- SPATIAL_TREE_3D_BENCHMARKS_END -->

## Interpreting the Results

All numbers represent **operations per second** (higher is better), except for construction times which show operations per second and absolute time.

### Choosing the Right Tree

**OctTree3D**:

- Best for: General-purpose 3D spatial queries
- Strengths: Balanced performance, easy to use, good spatial locality
- Use cases: 3D collision detection, visibility culling, spatial audio

**KdTree3D (Balanced)**:

- Best for: Nearest-neighbor queries in 3D space
- Strengths: Fast point queries, good for smaller datasets
- Use cases: Pathfinding, AI spatial awareness, particle systems

**KdTree3D (Unbalanced)**:

- Best for: When you need fast construction and will rebuild frequently
- Strengths: Fastest construction, similar query performance to balanced
- Use cases: Dynamic environments, frequently changing spatial data

**RTree3D**:

- Best for: 3D bounding box queries, especially with volumetric data
- Strengths: Excellent for large bounding volumes, handles overlapping objects
- Use cases: Physics engines, frustum culling, volumetric effects

### Important Notes

- All spatial trees assume **immutable** positional data
- If positions change, you must reconstruct the tree
- Spatial queries are O(log n) vs O(n) for linear search
- 3D trees have higher construction costs than 2D variants due to additional dimension
- Construction cost is amortized over many queries
