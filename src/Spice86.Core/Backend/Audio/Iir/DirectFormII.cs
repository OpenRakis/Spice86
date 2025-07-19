/*
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements.  See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License.  You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 * 
 *  Copyright (c) 2009 by Vinnie Falco
 *  Copyright (c) 2016 by Bernd Porr
 */

namespace Spice86.Core.Backend.Audio.Iir;

/**
 *
 * Implementation of a Direct Form II filter with its states. The coefficients
 * are supplied from the outside.
 *
 */

public class DirectFormII : DirectFormAbstract {
    public DirectFormII() {
        Reset();
    }

    public override void Reset() {
        _v1 = 0;
        _v2 = 0;
    }

    public override double Process1(double x,
                    Biquad s) {
        if (s != null) {
            double w = x - s.A1 * _v1 - s.A2 * _v2;
            double res = s.B0 * w + s.B1 * _v1 + s.B2 * _v2;

            _v2 = _v1;
            _v1 = w;

            return res;
        } else {
            return x;
        }
    }

    double _v1; // v[-1]
    double _v2; // v[-2]
}
